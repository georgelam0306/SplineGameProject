using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using Core;
using Derp.Doc.Model;
using Derp.Doc.Plugins;
using Derp.Doc.Storage;
using Derp.Doc.Tables;
using FixedMath;
using DerpDoc.Runtime;

namespace Derp.Doc.Export;

public sealed class DocExportPipeline
{
    private const string FormulaErrorText = "#ERR";
    private const string DefaultExportNamespace = "DerpDocDatabase";
    private const string VariantTableSuffixPrefix = "@v";
    private const string TableVariantKeySeparator = "|v";

    internal readonly struct ExportTableVariantSnapshot
    {
        public ExportTableVariantSnapshot(
            int variantId,
            string variantName,
            ExportTableModel exportTable)
        {
            VariantId = variantId;
            VariantName = variantName;
            ExportTable = exportTable;
        }

        public int VariantId { get; }
        public string VariantName { get; }
        public ExportTableModel ExportTable { get; }
    }

    public ExportPipelineResult ExportFromDirectory(string dbRoot, ExportPipelineOptions options)
    {
        var project = ProjectLoader.Load(dbRoot);
        return Export(project, options);
    }

    public ExportPipelineResult Export(DocProject project, ExportPipelineOptions options)
    {
        var result = new ExportPipelineResult();
        EnsureBuiltInExportProvidersRegistered();

        if (string.IsNullOrWhiteSpace(options.BinaryOutputPath))
        {
            result.Diagnostics.Add(new ExportDiagnostic(
                ExportDiagnosticSeverity.Error,
                "export/options/missing-binary-path",
                "BinaryOutputPath is required."));
            return result;
        }

        ValidateNoDerivedTableVariantDeltas(project, result.Diagnostics);
        if (result.HasErrors)
        {
            return result;
        }

        var baseSnapshotProject = DocExportSnapshotBuilder.BuildBase(project);
        List<ExportTableModel> baseExportTables = CollectExportTables(baseSnapshotProject, options, result.Diagnostics);
        if (result.HasErrors || baseExportTables.Count <= 0)
        {
            return result;
        }

        var baseFormulaEngine = new DocFormulaEngine();
        try
        {
            baseFormulaEngine.EvaluateProject(baseSnapshotProject);
        }
        catch (Exception ex)
        {
            result.Diagnostics.Add(new ExportDiagnostic(
                ExportDiagnosticSeverity.Error,
                "export/compute/exception",
                "Base: " + ex.Message));
            return result;
        }

        ValidateComputedState(baseExportTables, baseFormulaEngine, result.Diagnostics);
        if (result.HasErrors)
        {
            return result;
        }

        ValidateEnumNameCollisions(baseExportTables, result.Diagnostics);
        if (result.HasErrors)
        {
            return result;
        }

        var tableVariantSnapshots = BuildTableVariantSnapshots(project, options, baseExportTables, result.Diagnostics);
        if (result.HasErrors)
        {
            return result;
        }

        ValidateTableVariantExportShapes(baseExportTables, tableVariantSnapshots, result.Diagnostics);
        if (result.HasErrors)
        {
            return result;
        }

        var pkValueByTableAndVariant = BuildPrimaryKeyValueMaps(tableVariantSnapshots, result.Diagnostics);
        if (result.HasErrors)
        {
            return result;
        }

        var stringIdByValue = BuildStringRegistry(tableVariantSnapshots, result.Diagnostics);
        if (result.HasErrors)
        {
            return result;
        }

        var binaryWriter = new DerpDocBinaryWriter();
        for (int snapshotIndex = 0; snapshotIndex < tableVariantSnapshots.Count; snapshotIndex++)
        {
            ExportTableVariantSnapshot snapshot = tableVariantSnapshots[snapshotIndex];
            ExportTableModel exportTable = snapshot.ExportTable;
            string variantBinaryTableName = CreateVariantBinaryTableName(exportTable.BinaryTableName, snapshot.VariantId);
            AddTableAndIndexes(
                binaryWriter,
                exportTable,
                variantBinaryTableName,
                snapshot.VariantId,
                pkValueByTableAndVariant,
                stringIdByValue,
                result.Diagnostics);
            if (result.HasErrors)
            {
                return result;
            }
        }

        var registryEntries = new List<(uint Id, string Value)>(stringIdByValue.Count);
        foreach (var kvp in stringIdByValue)
        {
            registryEntries.Add((kvp.Value, kvp.Key));
        }
        registryEntries.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        binaryWriter.SetStringRegistry(registryEntries);

        result.Binary = binaryWriter.Build();

        string binaryFileName = Path.GetFileName(options.BinaryOutputPath);
        var generatedFiles = DerpDocCodeGenerator.Generate(baseExportTables, binaryFileName);
        for (int i = 0; i < generatedFiles.Count; i++)
        {
            result.GeneratedFiles.Add(generatedFiles[i]);
        }

        WriteOutputs(result, options, baseExportTables, tableVariantSnapshots);
        return result;
    }

    private static void EnsureBuiltInExportProvidersRegistered()
    {
        // No built-in custom providers are currently required for export.
    }

    private static void ValidateNoDerivedTableVariantDeltas(DocProject project, List<ExportDiagnostic> diagnostics)
    {
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable table = project.Tables[tableIndex];
            if (!table.IsDerived || table.VariantDeltas.Count <= 0)
            {
                continue;
            }

            for (int deltaIndex = 0; deltaIndex < table.VariantDeltas.Count; deltaIndex++)
            {
                int variantId = table.VariantDeltas[deltaIndex].VariantId;
                if (variantId == DocTableVariant.BaseVariantId)
                {
                    continue;
                }

                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/variant/derived-deltas-not-supported",
                    $"Derived table '{table.Name}' has variant delta data for variant '{variantId}'. Derived tables do not support variant deltas.",
                    TableId: table.Id));
                return;
            }
        }
    }

    internal static bool TryConvertNumberToInt(double value, out int result)
    {
        double rounded = Math.Round(value);
        if (Math.Abs(value - rounded) > 1e-9)
        {
            result = 0;
            return false;
        }

        if (rounded < int.MinValue || rounded > int.MaxValue)
        {
            result = 0;
            return false;
        }

        result = (int)rounded;
        return true;
    }

    private static void WriteOutputs(
        ExportPipelineResult result,
        ExportPipelineOptions options,
        List<ExportTableModel> exportTables,
        List<ExportTableVariantSnapshot> tableVariantSnapshots)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(options.BinaryOutputPath) ?? ".");
            File.WriteAllBytes(options.BinaryOutputPath, result.Binary);
        }
        catch (Exception ex)
        {
            result.Diagnostics.Add(new ExportDiagnostic(
                ExportDiagnosticSeverity.Error,
                "export/io/binary-write-failed",
                $"Failed to write binary output '{options.BinaryOutputPath}': {ex.Message}"));
            return;
        }

        if (!string.IsNullOrWhiteSpace(options.LiveBinaryOutputPath))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(options.LiveBinaryOutputPath) ?? ".");
                int tableCount = result.Binary.Length >= 12
                    ? (int)BinaryPrimitives.ReadUInt32LittleEndian(result.Binary.AsSpan(8, 4))
                    : 0;
                using var liveWriter = LiveBinaryWriter.CreateOrOpen(options.LiveBinaryOutputPath, tableCount, result.Binary.Length);
                liveWriter.Write(result.Binary);
            }
            catch (Exception ex)
            {
                result.Diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/io/live-binary-write-failed",
                    $"Failed to write live binary output '{options.LiveBinaryOutputPath}': {ex.Message}"));
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(options.GeneratedOutputDirectory))
        {
            try
            {
                Directory.CreateDirectory(options.GeneratedOutputDirectory);
                for (int i = 0; i < result.GeneratedFiles.Count; i++)
                {
                    var gf = result.GeneratedFiles[i];
                    File.WriteAllText(Path.Combine(options.GeneratedOutputDirectory, gf.FileName), gf.Content);
                }
            }
            catch (Exception ex)
            {
                result.Diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/io/generated-write-failed",
                    $"Failed to write generated output '{options.GeneratedOutputDirectory}': {ex.Message}"));
                return;
            }
        }

        if (options.WriteManifest)
        {
            try
            {
                var manifest = DerpDocManifest.Create(options, exportTables, tableVariantSnapshots, result.Diagnostics);
                var manifestPath = options.BinaryOutputPath + ".manifest.json";
                File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, DerpDocManifestJsonContext.Default.DerpDocManifest));
            }
            catch (Exception ex)
            {
                result.Diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/io/manifest-write-failed",
                    $"Failed to write manifest for '{options.BinaryOutputPath}': {ex.Message}"));
            }
        }
    }

    private static List<ExportTableModel> CollectExportTables(DocProject project, ExportPipelineOptions options, List<ExportDiagnostic> diagnostics)
    {
        var exportTables = new List<ExportTableModel>();
        var dbPropertyNames = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> includedTableIds = CollectIncludedExportTableIds(project);

        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            var table = project.Tables[tableIndex];
            if (!includedTableIds.Contains(table.Id))
            {
                continue;
            }

            EnsureSyntheticSubtablePrimaryKey(project, table);

            string ns = DefaultExportNamespace;

            string structName = table.ExportConfig != null && !string.IsNullOrWhiteSpace(table.ExportConfig.StructName)
                ? CSharpIdentifier.Sanitize(table.ExportConfig.StructName, "Row")
                : CSharpIdentifier.Sanitize(CSharpIdentifier.ToPascalCase(table.Name), "Row");

            string binaryTableName = structName;

            string baseDbPropertyName = CSharpIdentifier.Sanitize(CSharpIdentifier.ToPascalCase(table.Name), "Table");
            string dbPropertyName = baseDbPropertyName;
            int suffix = 2;
            while (!dbPropertyNames.Add(dbPropertyName))
            {
                dbPropertyName = baseDbPropertyName + suffix;
                suffix++;
            }

            // Compute set of key columns for Select enum encoding rules.
            var keyColumns = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(table.Keys.PrimaryKeyColumnId))
            {
                keyColumns.Add(table.Keys.PrimaryKeyColumnId);
            }
            for (int i = 0; i < table.Keys.SecondaryKeys.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(table.Keys.SecondaryKeys[i].ColumnId))
                {
                    keyColumns.Add(table.Keys.SecondaryKeys[i].ColumnId);
                }
            }

            var columns = new List<ExportColumnModel>();
            var fieldNameSet = new HashSet<string>(StringComparer.Ordinal);

            for (int colIndex = 0; colIndex < table.Columns.Count; colIndex++)
            {
                var col = table.Columns[colIndex];
                bool isSubtableParentColumn =
                    table.IsSubtable &&
                    !string.IsNullOrWhiteSpace(table.ParentRowColumnId) &&
                    string.Equals(col.Id, table.ParentRowColumnId, StringComparison.Ordinal);

                if (col.ExportIgnore && !isSubtableParentColumn)
                {
                    continue;
                }

                // Subtable columns express relationships and are exported via child tables/indexes.
                if (col.Kind == DocColumnKind.Subtable)
                {
                    continue;
                }

                string baseFieldName = CSharpIdentifier.Sanitize(CSharpIdentifier.ToPascalCase(col.Name), "Field");
                string fieldName = baseFieldName;
                int fieldSuffix = 2;
                while (!fieldNameSet.Add(fieldName))
                {
                    fieldName = baseFieldName + fieldSuffix;
                    fieldSuffix++;
                }

                var colModel = ExportColumnModel.Create(table, structName, col, fieldName, keyColumns, diagnostics);
                if (colModel != null)
                {
                    columns.Add(colModel.Value);
                }
            }

            var variables = CollectExportTableVariables(table, diagnostics);
            var viewBindings = CollectExportViewBindings(table, diagnostics);

            exportTables.Add(new ExportTableModel(
                project,
                table,
                ns,
                structName,
                binaryTableName,
                dbPropertyName,
                columns,
                variables,
                viewBindings));
        }

        if (exportTables.Count == 0)
        {
            diagnostics.Add(new ExportDiagnostic(
                ExportDiagnosticSeverity.Error,
                "export/config/no-export-tables",
                "No tables are marked for export."));
            return exportTables;
        }

        for (int i = 0; i < exportTables.Count; i++)
        {
            exportTables[i].BindKeys(diagnostics);
        }

        BuildSubtableLinks(exportTables, diagnostics);
        BuildRowReferenceModels(exportTables, diagnostics);

        return exportTables;
    }

    private static HashSet<string> CollectIncludedExportTableIds(DocProject project)
    {
        var includedTableIds = new HashSet<string>(StringComparer.Ordinal);
        var tableById = new Dictionary<string, DocTable>(project.Tables.Count, StringComparer.Ordinal);
        var queue = new Queue<string>();

        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable table = project.Tables[tableIndex];
            tableById[table.Id] = table;
            if (table.ExportConfig != null && table.ExportConfig.Enabled && includedTableIds.Add(table.Id))
            {
                queue.Enqueue(table.Id);
            }
        }

        while (queue.Count > 0)
        {
            string parentTableId = queue.Dequeue();
            if (!tableById.TryGetValue(parentTableId, out DocTable? parentTable))
            {
                continue;
            }

            for (int columnIndex = 0; columnIndex < parentTable.Columns.Count; columnIndex++)
            {
                DocColumn column = parentTable.Columns[columnIndex];
                if (column.Kind != DocColumnKind.Subtable || string.IsNullOrWhiteSpace(column.SubtableId))
                {
                    continue;
                }

                if (includedTableIds.Add(column.SubtableId))
                {
                    queue.Enqueue(column.SubtableId);
                }
            }
        }

        return includedTableIds;
    }

    private static void EnsureSyntheticSubtablePrimaryKey(DocProject project, DocTable table)
    {
        if (!table.IsSubtable || !string.IsNullOrWhiteSpace(table.Keys.PrimaryKeyColumnId))
        {
            return;
        }

        const string syntheticColumnId = "__export_row_id";
        DocColumn? syntheticColumn = null;
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            if (string.Equals(table.Columns[columnIndex].Id, syntheticColumnId, StringComparison.Ordinal))
            {
                syntheticColumn = table.Columns[columnIndex];
                break;
            }
        }

        if (syntheticColumn == null)
        {
            syntheticColumn = new DocColumn
            {
                Id = syntheticColumnId,
                Name = "__RowId",
                Kind = DocColumnKind.Id,
                ColumnTypeId = DocColumnTypeIds.Id,
                IsHidden = true,
            };
            table.Columns.Insert(0, syntheticColumn);
        }

        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            DocRow row = table.Rows[rowIndex];
            row.SetCell(syntheticColumn.Id, DocCellValue.Text(row.Id));
        }

        table.Keys.PrimaryKeyColumnId = syntheticColumn.Id;
    }

    private static void BuildSubtableLinks(List<ExportTableModel> exportTables, List<ExportDiagnostic> diagnostics)
    {
        var exportTableById = new Dictionary<string, ExportTableModel>(exportTables.Count, StringComparer.Ordinal);
        for (int tableIndex = 0; tableIndex < exportTables.Count; tableIndex++)
        {
            ExportTableModel exportTable = exportTables[tableIndex];
            exportTableById[exportTable.Table.Id] = exportTable;
            exportTable.SubtableChildren.Clear();
            exportTable.SubtableParent = null;
        }

        for (int parentIndex = 0; parentIndex < exportTables.Count; parentIndex++)
        {
            ExportTableModel parent = exportTables[parentIndex];
            for (int columnIndex = 0; columnIndex < parent.Table.Columns.Count; columnIndex++)
            {
                DocColumn subtableColumn = parent.Table.Columns[columnIndex];
                if (subtableColumn.Kind != DocColumnKind.Subtable ||
                    string.IsNullOrWhiteSpace(subtableColumn.SubtableId))
                {
                    continue;
                }

                if (!exportTableById.TryGetValue(subtableColumn.SubtableId, out ExportTableModel? child))
                {
                    diagnostics.Add(new ExportDiagnostic(
                        ExportDiagnosticSeverity.Error,
                        "export/subtable/child-not-exported",
                        $"Subtable '{parent.Table.Name}.{subtableColumn.Name}' targets table '{subtableColumn.SubtableId}' which is not exported.",
                        TableId: parent.Table.Id,
                        ColumnId: subtableColumn.Id));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(child.Table.ParentRowColumnId))
                {
                    diagnostics.Add(new ExportDiagnostic(
                        ExportDiagnosticSeverity.Error,
                        "export/subtable/child-missing-parent-column",
                        $"Subtable child table '{child.Table.Name}' has no ParentRowColumnId.",
                        TableId: child.Table.Id));
                    continue;
                }

                DocColumn? childParentRowColumn = null;
                for (int childColumnIndex = 0; childColumnIndex < child.Table.Columns.Count; childColumnIndex++)
                {
                    DocColumn childColumn = child.Table.Columns[childColumnIndex];
                    if (string.Equals(childColumn.Id, child.Table.ParentRowColumnId, StringComparison.Ordinal))
                    {
                        childParentRowColumn = childColumn;
                        break;
                    }
                }

                if (childParentRowColumn == null)
                {
                    diagnostics.Add(new ExportDiagnostic(
                        ExportDiagnosticSeverity.Error,
                        "export/subtable/child-parent-column-not-found",
                        $"Subtable child table '{child.Table.Name}' parent column '{child.Table.ParentRowColumnId}' was not found.",
                        TableId: child.Table.Id));
                    continue;
                }

                string propertyName = CSharpIdentifier.Sanitize(
                    CSharpIdentifier.ToPascalCase(subtableColumn.Name),
                    "Subtable");

                var link = new ExportSubtableLinkModel(
                    parent,
                    subtableColumn,
                    child,
                    childParentRowColumn,
                    propertyName);
                parent.SubtableChildren.Add(link);

                if (child.SubtableParent != null)
                {
                    diagnostics.Add(new ExportDiagnostic(
                        ExportDiagnosticSeverity.Error,
                        "export/subtable/multi-parent-not-supported",
                        $"Subtable child table '{child.Table.Name}' is referenced by multiple parent subtable columns.",
                        TableId: child.Table.Id));
                    continue;
                }

                child.SubtableParent = link;
            }
        }
    }

    private static void BuildRowReferenceModels(List<ExportTableModel> exportTables, List<ExportDiagnostic> diagnostics)
    {
        if (exportTables.Count <= 0)
        {
            return;
        }

        DocProject project = exportTables[0].Project;
        var projectTableById = new Dictionary<string, DocTable>(project.Tables.Count, StringComparer.Ordinal);
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable table = project.Tables[tableIndex];
            projectTableById[table.Id] = table;
        }

        for (int tableIndex = 0; tableIndex < exportTables.Count; tableIndex++)
        {
            ExportTableModel exportTable = exportTables[tableIndex];
            exportTable.RowReferences.Clear();

            if (exportTable.SubtableParent == null)
            {
                continue;
            }

            var rowIdColumns = new List<DocColumn>(2);
            for (int columnIndex = 0; columnIndex < exportTable.Table.Columns.Count; columnIndex++)
            {
                DocColumn column = exportTable.Table.Columns[columnIndex];
                string tableRefColumnId = ResolveRowRefTableRefColumnId(exportTable.Table, column);
                if (!string.IsNullOrWhiteSpace(tableRefColumnId))
                {
                    rowIdColumns.Add(column);
                }
            }

            if (rowIdColumns.Count <= 0)
            {
                continue;
            }

            if (rowIdColumns.Count > 1)
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/rowref/multiple-not-supported",
                    $"Table '{exportTable.Table.Name}' has multiple row-reference columns. Only one row-reference pair per table is currently supported.",
                    TableId: exportTable.Table.Id));
                continue;
            }

            DocColumn rowIdColumn = rowIdColumns[0];
            string tableRefColumnIdValue = ResolveRowRefTableRefColumnId(exportTable.Table, rowIdColumn);
            if (string.IsNullOrWhiteSpace(tableRefColumnIdValue))
            {
                continue;
            }

            DocColumn? tableRefColumn = FindColumnById(exportTable.Table, tableRefColumnIdValue);
            if (tableRefColumn == null || tableRefColumn.Kind != DocColumnKind.TableRef)
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/rowref/invalid-table-ref-column",
                    $"Row-reference column '{exportTable.Table.Name}.{rowIdColumn.Name}' references missing/invalid TableRef column '{tableRefColumnIdValue}'.",
                    TableId: exportTable.Table.Id,
                    ColumnId: rowIdColumn.Id));
                continue;
            }

            if (!TryFindExportColumn(exportTable, rowIdColumn.Id, out ExportColumnModel rowIdExportColumn) ||
                rowIdExportColumn.FieldKind != ExportFieldKind.StringHandle)
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/rowref/row-column-not-exported",
                    $"Row-reference id column '{exportTable.Table.Name}.{rowIdColumn.Name}' must be exported as StringHandle.",
                    TableId: exportTable.Table.Id,
                    ColumnId: rowIdColumn.Id));
                continue;
            }

            if (!TryFindExportColumn(exportTable, tableRefColumn.Id, out ExportColumnModel tableRefExportColumn) ||
                tableRefExportColumn.FieldKind != ExportFieldKind.StringHandle)
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/rowref/table-column-not-exported",
                    $"Row-reference table column '{exportTable.Table.Name}.{tableRefColumn.Name}' must be exported as StringHandle.",
                    TableId: exportTable.Table.Id,
                    ColumnId: tableRefColumn.Id));
                continue;
            }

            List<ExportRowReferenceTargetModel> targets = BuildRowReferenceTargets(
                exportTables,
                projectTableById,
                exportTable,
                tableRefColumn);

            if (targets.Count <= 0)
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/rowref/no-targets",
                    $"Row-reference column '{exportTable.Table.Name}.{rowIdColumn.Name}' has no exported target tables with supported primary keys.",
                    TableId: exportTable.Table.Id,
                    ColumnId: rowIdColumn.Id));
                continue;
            }

            string rowRefName = BuildRowReferenceName(rowIdColumn);
            string propertyName = CSharpIdentifier.Sanitize(CSharpIdentifier.ToPascalCase(rowRefName), "RowRef");
            exportTable.RowReferences.Add(new ExportRowReferenceModel(
                rowRefName,
                propertyName,
                tableRefColumn,
                rowIdColumn,
                tableRefExportColumn,
                rowIdExportColumn,
                targets));
        }
    }

    private static List<ExportRowReferenceTargetModel> BuildRowReferenceTargets(
        List<ExportTableModel> exportTables,
        Dictionary<string, DocTable> projectTableById,
        ExportTableModel ownerTable,
        DocColumn tableRefColumn)
    {
        string baseTableId = tableRefColumn.TableRefBaseTableId ?? "";
        if (string.IsNullOrWhiteSpace(baseTableId) &&
            string.Equals(ownerTable.Table.PluginTableTypeId, SplineGameLevelIds.EntitiesTableTypeId, StringComparison.Ordinal) &&
            string.Equals(tableRefColumn.Id, SplineGameLevelIds.EntitiesTableRefColumnId, StringComparison.Ordinal))
        {
            baseTableId = FindSystemTableIdByKey(ownerTable.Project, SplineGameLevelIds.SystemEntityBaseTableKey);
        }
        var candidates = new List<ExportTableModel>(exportTables.Count);
        for (int tableIndex = 0; tableIndex < exportTables.Count; tableIndex++)
        {
            ExportTableModel candidate = exportTables[tableIndex];
            if (candidate.PrimaryKey == null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(baseTableId) &&
                !IsTableDerivedFromOrEqualTo(projectTableById, candidate.Table.Id, baseTableId))
            {
                continue;
            }

            candidates.Add(candidate);
        }

        candidates.Sort(static (left, right) => string.Compare(left.DbPropertyName, right.DbPropertyName, StringComparison.Ordinal));

        var targets = new List<ExportRowReferenceTargetModel>(candidates.Count);
        for (int targetIndex = 0; targetIndex < candidates.Count; targetIndex++)
        {
            ExportTableModel target = candidates[targetIndex];
            string propertyName = CSharpIdentifier.Sanitize(CSharpIdentifier.ToPascalCase(target.DbPropertyName), "Target");
            targets.Add(new ExportRowReferenceTargetModel(
                tag: targetIndex + 1,
                tagIndex: targetIndex,
                targetTable: target,
                propertyName: propertyName));
        }

        return targets;
    }

    private static string FindSystemTableIdByKey(DocProject project, string systemKey)
    {
        if (string.IsNullOrWhiteSpace(systemKey))
        {
            return "";
        }

        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable table = project.Tables[tableIndex];
            if (string.Equals(table.SystemKey, systemKey, StringComparison.Ordinal))
            {
                return table.Id;
            }
        }

        return "";
    }

    private static bool IsTableDerivedFromOrEqualTo(
        Dictionary<string, DocTable> projectTableById,
        string candidateTableId,
        string baseTableId)
    {
        if (string.Equals(candidateTableId, baseTableId, StringComparison.Ordinal))
        {
            return true;
        }

        string currentTableId = candidateTableId;
        const int maxDepth = 64;
        for (int depth = 0; depth < maxDepth; depth++)
        {
            if (!projectTableById.TryGetValue(currentTableId, out DocTable? currentTable))
            {
                return false;
            }

            string nextTableId = "";
            if (currentTable.IsDerived &&
                currentTable.DerivedConfig != null &&
                !string.IsNullOrWhiteSpace(currentTable.DerivedConfig.BaseTableId))
            {
                nextTableId = currentTable.DerivedConfig.BaseTableId;
            }
            else if (!string.IsNullOrWhiteSpace(currentTable.SchemaSourceTableId))
            {
                nextTableId = currentTable.SchemaSourceTableId;
            }
            else if (!string.IsNullOrWhiteSpace(currentTable.InheritanceSourceTableId))
            {
                nextTableId = currentTable.InheritanceSourceTableId;
            }

            if (string.IsNullOrWhiteSpace(nextTableId))
            {
                return false;
            }

            if (string.Equals(nextTableId, baseTableId, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(nextTableId, currentTableId, StringComparison.Ordinal))
            {
                return false;
            }

            currentTableId = nextTableId;
        }

        return false;
    }

    private static bool TryFindExportColumn(ExportTableModel exportTable, string sourceColumnId, out ExportColumnModel exportColumn)
    {
        for (int columnIndex = 0; columnIndex < exportTable.Columns.Count; columnIndex++)
        {
            ExportColumnModel candidate = exportTable.Columns[columnIndex];
            if (string.Equals(candidate.SourceColumn.Id, sourceColumnId, StringComparison.Ordinal))
            {
                exportColumn = candidate;
                return true;
            }
        }

        exportColumn = default;
        return false;
    }

    private static DocColumn? FindColumnById(DocTable table, string columnId)
    {
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn candidate = table.Columns[columnIndex];
            if (string.Equals(candidate.Id, columnId, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string ResolveRowRefTableRefColumnId(DocTable table, DocColumn rowIdColumn)
    {
        if (!string.IsNullOrWhiteSpace(rowIdColumn.RowRefTableRefColumnId))
        {
            return rowIdColumn.RowRefTableRefColumnId;
        }

        if (string.Equals(table.PluginTableTypeId, SplineGameLevelIds.EntitiesTableTypeId, StringComparison.Ordinal) &&
            string.Equals(rowIdColumn.Id, SplineGameLevelIds.EntitiesRowIdColumnId, StringComparison.Ordinal))
        {
            return SplineGameLevelIds.EntitiesTableRefColumnId;
        }

        return "";
    }

    private static string BuildRowReferenceName(DocColumn rowIdColumn)
    {
        string normalized = CSharpIdentifier.ToPascalCase(rowIdColumn.Name);
        if (normalized.EndsWith("RowId", StringComparison.Ordinal))
        {
            normalized = normalized.Substring(0, normalized.Length - "RowId".Length);
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "RowRef";
        }

        return CSharpIdentifier.Sanitize(normalized, "RowRef");
    }

    private static List<ExportTableVariantSnapshot> BuildTableVariantSnapshots(
        DocProject sourceProject,
        ExportPipelineOptions options,
        List<ExportTableModel> baseExportTables,
        List<ExportDiagnostic> diagnostics)
    {
        var snapshots = new List<ExportTableVariantSnapshot>();

        // Base snapshot: export all base tables.
        var baseSnapshotProject = DocExportSnapshotBuilder.BuildBase(sourceProject);
        var baseFormulaEngine = new DocFormulaEngine();
        baseFormulaEngine.EvaluateProject(baseSnapshotProject);
        var baseExportTablesSnapshot = CollectExportTables(baseSnapshotProject, options, diagnostics);
        if (HasError(diagnostics))
        {
            return snapshots;
        }

        ValidateComputedState(baseExportTablesSnapshot, baseFormulaEngine, diagnostics);
        if (HasError(diagnostics))
        {
            return snapshots;
        }

        for (int tableIndex = 0; tableIndex < baseExportTablesSnapshot.Count; tableIndex++)
        {
            snapshots.Add(new ExportTableVariantSnapshot(
                DocTableVariant.BaseVariantId,
                DocTableVariant.BaseVariantName,
                baseExportTablesSnapshot[tableIndex]));
        }

        // Non-base variants: export each table variant as its own snapshot.
        for (int tableIndex = 0; tableIndex < baseExportTables.Count; tableIndex++)
        {
            ExportTableModel baseExportTable = baseExportTables[tableIndex];
            DocTable baseTable = baseExportTable.Table;
            for (int variantIndex = 0; variantIndex < baseTable.Variants.Count; variantIndex++)
            {
                DocTableVariant variant = baseTable.Variants[variantIndex];
                if (variant.Id == DocTableVariant.BaseVariantId)
                {
                    continue;
                }

                DocProject snapshotProject;
                try
                {
                    snapshotProject = DocExportSnapshotBuilder.BuildWithTableVariant(sourceProject, baseTable.Id, variant.Id);
                }
                catch (Exception ex)
                {
                    diagnostics.Add(new ExportDiagnostic(
                        ExportDiagnosticSeverity.Error,
                        "export/variant/snapshot-failed",
                        $"Failed to build table '{baseTable.Name}' variant '{variant.Name}' ({variant.Id.ToString(CultureInfo.InvariantCulture)}): {ex.Message}",
                        TableId: baseTable.Id));
                    return snapshots;
                }

                List<ExportTableModel> exportTables = CollectExportTables(snapshotProject, options, diagnostics);
                if (HasError(diagnostics))
                {
                    return snapshots;
                }

                var formulaEngine = new DocFormulaEngine();
                try
                {
                    formulaEngine.EvaluateProject(snapshotProject);
                }
                catch (Exception ex)
                {
                    diagnostics.Add(new ExportDiagnostic(
                        ExportDiagnosticSeverity.Error,
                        "export/compute/exception",
                        $"Table '{baseTable.Name}' variant '{variant.Name}' ({variant.Id.ToString(CultureInfo.InvariantCulture)}): {ex.Message}",
                        TableId: baseTable.Id));
                    return snapshots;
                }

                ValidateComputedState(exportTables, formulaEngine, diagnostics);
                if (HasError(diagnostics))
                {
                    return snapshots;
                }

                ExportTableModel? matchingTable = null;
                for (int exportedIndex = 0; exportedIndex < exportTables.Count; exportedIndex++)
                {
                    if (string.Equals(exportTables[exportedIndex].Table.Id, baseTable.Id, StringComparison.Ordinal))
                    {
                        matchingTable = exportTables[exportedIndex];
                        break;
                    }
                }

                if (matchingTable == null)
                {
                    diagnostics.Add(new ExportDiagnostic(
                        ExportDiagnosticSeverity.Error,
                        "export/variant/table-missing",
                        $"Variant snapshot missing exported table '{baseTable.Name}'.",
                        TableId: baseTable.Id));
                    return snapshots;
                }

                snapshots.Add(new ExportTableVariantSnapshot(
                    variant.Id,
                    variant.Name,
                    matchingTable));
            }
        }

        return snapshots;
    }

    private static void ValidateTableVariantExportShapes(
        List<ExportTableModel> baseExportTables,
        List<ExportTableVariantSnapshot> tableVariantSnapshots,
        List<ExportDiagnostic> diagnostics)
    {
        var baseTableById = new Dictionary<string, ExportTableModel>(baseExportTables.Count, StringComparer.Ordinal);
        for (int tableIndex = 0; tableIndex < baseExportTables.Count; tableIndex++)
        {
            baseTableById[baseExportTables[tableIndex].Table.Id] = baseExportTables[tableIndex];
        }

        for (int snapshotIndex = 0; snapshotIndex < tableVariantSnapshots.Count; snapshotIndex++)
        {
            ExportTableVariantSnapshot snapshot = tableVariantSnapshots[snapshotIndex];
            if (snapshot.VariantId == DocTableVariant.BaseVariantId)
            {
                continue;
            }

            ExportTableModel variantTable = snapshot.ExportTable;
            if (!baseTableById.TryGetValue(variantTable.Table.Id, out ExportTableModel? baseTable))
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/variant/table-missing",
                    $"Variant '{snapshot.VariantName}' is missing exported base table '{variantTable.Table.Name}'.",
                    TableId: variantTable.Table.Id));
                return;
            }

            if (!string.Equals(baseTable.BinaryTableName, variantTable.BinaryTableName, StringComparison.Ordinal) ||
                baseTable.Columns.Count != variantTable.Columns.Count)
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/variant/table-schema-mismatch",
                    $"Variant '{snapshot.VariantName}' changed exported schema for table '{variantTable.Table.Name}'.",
                    TableId: variantTable.Table.Id));
                return;
            }
        }
    }

    private static string CreateVariantBinaryTableName(string baseBinaryTableName, int variantId)
    {
        if (variantId == DocTableVariant.BaseVariantId)
        {
            return baseBinaryTableName;
        }

        return baseBinaryTableName + VariantTableSuffixPrefix + variantId.ToString(CultureInfo.InvariantCulture);
    }

    private static string CreateTableVariantKey(string tableId, int variantId)
    {
        return tableId + TableVariantKeySeparator + variantId.ToString(CultureInfo.InvariantCulture);
    }

    private static List<ExportTableVariableModel> CollectExportTableVariables(
        DocTable table,
        List<ExportDiagnostic> diagnostics)
    {
        var exportVariables = new List<ExportTableVariableModel>(table.Variables.Count);
        var slotNames = new HashSet<string>(StringComparer.Ordinal);
        var variableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int variableIndex = 0; variableIndex < table.Variables.Count; variableIndex++)
        {
            DocTableVariable variable = table.Variables[variableIndex];
            if (string.IsNullOrWhiteSpace(variable.Name))
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/variables/invalid-name",
                    $"Table variable at index {variableIndex} in '{table.Name}' has an empty name.",
                    TableId: table.Id));
                continue;
            }

            if (!variableNames.Add(variable.Name))
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/variables/duplicate-name",
                    $"Duplicate table variable name '{variable.Name}' in '{table.Name}'.",
                    TableId: table.Id));
                continue;
            }

            string baseSlotName = CSharpIdentifier.Sanitize(CSharpIdentifier.ToPascalCase(variable.Name), "Variable");
            string slotName = baseSlotName;
            int slotSuffix = 2;
            while (!slotNames.Add(slotName))
            {
                slotName = baseSlotName + slotSuffix;
                slotSuffix++;
            }

            exportVariables.Add(new ExportTableVariableModel
            {
                Id = variable.Id,
                Name = variable.Name,
                SlotName = slotName,
                Kind = variable.Kind,
                ColumnTypeId = DocColumnTypeIdMapper.Resolve(variable.ColumnTypeId, variable.Kind),
                Expression = variable.Expression ?? "",
            });
        }

        return exportVariables;
    }

    private static List<ExportViewBindingModel> CollectExportViewBindings(
        DocTable table,
        List<ExportDiagnostic> diagnostics)
    {
        var bindings = new List<ExportViewBindingModel>(16);
        var outputNames = new HashSet<string>(StringComparer.Ordinal);

        for (int viewIndex = 0; viewIndex < table.Views.Count; viewIndex++)
        {
            DocView view = table.Views[viewIndex];

            AddViewBinding(
                table,
                view,
                ExportViewBindingTargetKind.GroupByColumn,
                targetItemId: "",
                "GroupByColumn",
                view.GroupByColumnBinding,
                bindings,
                outputNames,
                diagnostics);

            AddViewBinding(
                table,
                view,
                ExportViewBindingTargetKind.CalendarDateColumn,
                targetItemId: "",
                "CalendarDateColumn",
                view.CalendarDateColumnBinding,
                bindings,
                outputNames,
                diagnostics);

            AddViewBinding(
                table,
                view,
                ExportViewBindingTargetKind.ChartKind,
                targetItemId: "",
                "ChartKind",
                view.ChartKindBinding,
                bindings,
                outputNames,
                diagnostics);

            AddViewBinding(
                table,
                view,
                ExportViewBindingTargetKind.ChartCategoryColumn,
                targetItemId: "",
                "ChartCategoryColumn",
                view.ChartCategoryColumnBinding,
                bindings,
                outputNames,
                diagnostics);

            AddViewBinding(
                table,
                view,
                ExportViewBindingTargetKind.ChartValueColumn,
                targetItemId: "",
                "ChartValueColumn",
                view.ChartValueColumnBinding,
                bindings,
                outputNames,
                diagnostics);

            for (int sortIndex = 0; sortIndex < view.Sorts.Count; sortIndex++)
            {
                DocViewSort sort = view.Sorts[sortIndex];
                string sortTargetItemId = string.IsNullOrWhiteSpace(sort.Id)
                    ? "sort_" + sortIndex.ToString(CultureInfo.InvariantCulture)
                    : sort.Id;

                AddViewBinding(
                    table,
                    view,
                    ExportViewBindingTargetKind.SortColumn,
                    sortTargetItemId,
                    "SortColumn_" + sortIndex.ToString(CultureInfo.InvariantCulture),
                    sort.ColumnIdBinding,
                    bindings,
                    outputNames,
                    diagnostics);

                AddViewBinding(
                    table,
                    view,
                    ExportViewBindingTargetKind.SortDescending,
                    sortTargetItemId,
                    "SortDescending_" + sortIndex.ToString(CultureInfo.InvariantCulture),
                    sort.DescendingBinding,
                    bindings,
                    outputNames,
                    diagnostics);
            }

            for (int filterIndex = 0; filterIndex < view.Filters.Count; filterIndex++)
            {
                DocViewFilter filter = view.Filters[filterIndex];
                string filterTargetItemId = string.IsNullOrWhiteSpace(filter.Id)
                    ? "filter_" + filterIndex.ToString(CultureInfo.InvariantCulture)
                    : filter.Id;

                AddViewBinding(
                    table,
                    view,
                    ExportViewBindingTargetKind.FilterColumn,
                    filterTargetItemId,
                    "FilterColumn_" + filterIndex.ToString(CultureInfo.InvariantCulture),
                    filter.ColumnIdBinding,
                    bindings,
                    outputNames,
                    diagnostics);

                AddViewBinding(
                    table,
                    view,
                    ExportViewBindingTargetKind.FilterOperator,
                    filterTargetItemId,
                    "FilterOperator_" + filterIndex.ToString(CultureInfo.InvariantCulture),
                    filter.OpBinding,
                    bindings,
                    outputNames,
                    diagnostics);

                AddViewBinding(
                    table,
                    view,
                    ExportViewBindingTargetKind.FilterValue,
                    filterTargetItemId,
                    "FilterValue_" + filterIndex.ToString(CultureInfo.InvariantCulture),
                    filter.ValueBinding,
                    bindings,
                    outputNames,
                    diagnostics);
            }
        }

        return bindings;
    }

    private static void AddViewBinding(
        DocTable table,
        DocView view,
        ExportViewBindingTargetKind targetKind,
        string targetItemId,
        string outputNameSuffix,
        DocViewBinding? binding,
        List<ExportViewBindingModel> bindings,
        HashSet<string> outputNames,
        List<ExportDiagnostic> diagnostics)
    {
        if (binding == null || binding.IsEmpty)
        {
            return;
        }

        string expression = NormalizeBindingExpression(binding);
        if (string.IsNullOrWhiteSpace(expression))
        {
            return;
        }

        string outputNameBase = CSharpIdentifier.Sanitize(
            CSharpIdentifier.ToPascalCase(view.Name) + outputNameSuffix,
            "BindingOutput");
        string outputName = outputNameBase;
        int suffix = 2;
        while (!outputNames.Add(outputName))
        {
            outputName = outputNameBase + suffix.ToString(CultureInfo.InvariantCulture);
            suffix++;
        }

        bindings.Add(new ExportViewBindingModel
        {
            ViewId = view.Id,
            OutputName = outputName,
            TargetKind = targetKind,
            TargetItemId = targetItemId,
            Expression = expression,
        });

        DocColumnKind expectedKind = ResolveBindingExpectedKind(targetKind);
        if (binding.VariableName.Length > 0)
        {
            bool variableExists = false;
            DocColumnKind actualKind = DocColumnKind.Text;
            for (int variableIndex = 0; variableIndex < table.Variables.Count; variableIndex++)
            {
                if (string.Equals(table.Variables[variableIndex].Name, binding.VariableName, StringComparison.OrdinalIgnoreCase))
                {
                    variableExists = true;
                    actualKind = table.Variables[variableIndex].Kind;
                    break;
                }
            }

            if (!variableExists)
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/bindings/missing-variable",
                    $"Binding references missing table variable '{binding.VariableName}' on '{table.Name}'.",
                    TableId: table.Id));
            }
            else if (!IsBindingKindCompatible(expectedKind, actualKind))
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/bindings/type-mismatch",
                    $"Binding '{outputName}' expects {expectedKind} but variable '{binding.VariableName}' is {actualKind}.",
                    TableId: table.Id));
            }
        }

        if (!string.IsNullOrWhiteSpace(binding.FormulaExpression))
        {
            ValidateBindingFormulaType(table, outputName, binding.FormulaExpression, expectedKind, diagnostics);
        }
    }

    private static string NormalizeBindingExpression(DocViewBinding binding)
    {
        if (!string.IsNullOrWhiteSpace(binding.FormulaExpression))
        {
            return binding.FormulaExpression.Trim();
        }

        if (!string.IsNullOrWhiteSpace(binding.VariableName))
        {
            return "thisTable." + binding.VariableName.Trim();
        }

        return "";
    }

    private static DocColumnKind ResolveBindingExpectedKind(ExportViewBindingTargetKind targetKind)
    {
        if (targetKind == ExportViewBindingTargetKind.SortDescending)
        {
            return DocColumnKind.Checkbox;
        }

        return DocColumnKind.Text;
    }

    private static bool IsBindingKindCompatible(DocColumnKind expectedKind, DocColumnKind variableKind)
    {
        if (expectedKind == DocColumnKind.Checkbox)
        {
            return variableKind == DocColumnKind.Checkbox;
        }

        return variableKind != DocColumnKind.Number && variableKind != DocColumnKind.Checkbox;
    }

    private static void ValidateBindingFormulaType(
        DocTable table,
        string outputName,
        string formulaExpression,
        DocColumnKind expectedKind,
        List<ExportDiagnostic> diagnostics)
    {
        string expression = formulaExpression.Trim();
        if (expression.Length <= 0)
        {
            return;
        }

        if (expectedKind == DocColumnKind.Checkbox)
        {
            if (string.Equals(expression, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(expression, "false", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        else
        {
            if (expression.Length >= 2 &&
                expression[0] == '"' &&
                expression[^1] == '"')
            {
                return;
            }
        }

        const string thisTablePrefix = "thisTable.";
        if (!expression.StartsWith(thisTablePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string variableName = expression.Substring(thisTablePrefix.Length).Trim();
        if (variableName.Length <= 0)
        {
            return;
        }

        for (int variableIndex = 0; variableIndex < table.Variables.Count; variableIndex++)
        {
            DocTableVariable variable = table.Variables[variableIndex];
            if (!string.Equals(variable.Name, variableName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsBindingKindCompatible(expectedKind, variable.Kind))
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/bindings/type-mismatch",
                    $"Binding '{outputName}' expects {expectedKind} but formula references {variable.Kind} variable '{variableName}'.",
                    TableId: table.Id));
            }

            return;
        }
    }

    private static void ValidateEnumNameCollisions(List<ExportTableModel> exportTables, List<ExportDiagnostic> diagnostics)
    {
        var signaturesByEnumName = new Dictionary<string, string>(StringComparer.Ordinal);

        for (int i = 0; i < exportTables.Count; i++)
        {
            var t = exportTables[i];
            for (int c = 0; c < t.Columns.Count; c++)
            {
                var enumModel = t.Columns[c].EnumModel;
                if (enumModel == null)
                {
                    continue;
                }

                RegisterEnumSignature(enumModel, signaturesByEnumName, diagnostics);
            }

            if (t.PrimaryKey?.EnumModel != null)
            {
                RegisterEnumSignature(t.PrimaryKey.EnumModel, signaturesByEnumName, diagnostics);
            }

            for (int s = 0; s < t.SecondaryKeys.Count; s++)
            {
                if (t.SecondaryKeys[s].EnumModel != null)
                {
                    RegisterEnumSignature(t.SecondaryKeys[s].EnumModel!, signaturesByEnumName, diagnostics);
                }
            }
        }
    }

    private static void RegisterEnumSignature(
        ExportEnumModel model,
        Dictionary<string, string> signaturesByEnumName,
        List<ExportDiagnostic> diagnostics)
    {
        // Stable signature: key-ness + option list.
        var signature = (model.IsKey ? "key|" : "opt|") + string.Join("\u001F", model.Options);
        if (!signaturesByEnumName.TryGetValue(model.EnumName, out var existing))
        {
            signaturesByEnumName[model.EnumName] = signature;
            return;
        }

        if (!string.Equals(existing, signature, StringComparison.Ordinal))
        {
            diagnostics.Add(new ExportDiagnostic(
                ExportDiagnosticSeverity.Error,
                "export/enum/name-collision",
                $"Enum name collision for '{model.EnumName}'. Use ExportEnumName to disambiguate.",
                model.TableId,
                model.ColumnId));
        }
    }

    private static void ValidateComputedState(
        List<ExportTableModel> exportTables,
        DocFormulaEngine formulaEngine,
        List<ExportDiagnostic> diagnostics)
    {
        for (int i = 0; i < exportTables.Count; i++)
        {
            var tableModel = exportTables[i];
            var table = tableModel.Table;

            if (table.IsDerived &&
                formulaEngine.DerivedResults.TryGetValue(table.Id, out var derivedResult))
            {
                if (derivedResult.MultiMatchCount > 0)
                {
                    diagnostics.Add(new ExportDiagnostic(
                        ExportDiagnosticSeverity.Error,
                        "export/derived/multimatch",
                        $"Derived table '{table.Name}' has MultiMatch join results ({derivedResult.MultiMatchCount}). Export is strict in Phase 5.",
                        TableId: table.Id));
                }

                if (derivedResult.TypeMismatchCount > 0)
                {
                    diagnostics.Add(new ExportDiagnostic(
                        ExportDiagnosticSeverity.Error,
                        "export/derived/typemismatch",
                        $"Derived table '{table.Name}' has join configuration type mismatches ({derivedResult.TypeMismatchCount}).",
                        TableId: table.Id));
                }
            }

            for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                var row = table.Rows[rowIndex];
                for (int colIndex = 0; colIndex < table.Columns.Count; colIndex++)
                {
                    var col = table.Columns[colIndex];
                    if (string.IsNullOrWhiteSpace(col.FormulaExpression))
                    {
                        continue;
                    }

                    if (row.Cells.TryGetValue(col.Id, out var cell) &&
                        string.Equals(cell.StringValue, FormulaErrorText, StringComparison.Ordinal))
                    {
                        diagnostics.Add(new ExportDiagnostic(
                            ExportDiagnosticSeverity.Error,
                            "export/formula/error",
                            $"Formula error in table '{table.Name}', column '{col.Name}', row '{row.Id}'.",
                            TableId: table.Id,
                            ColumnId: col.Id));
                        return;
                    }
                }
            }
        }
    }

    private static Dictionary<string, Dictionary<string, int>> BuildPrimaryKeyValueMaps(
        List<ExportTableVariantSnapshot> snapshots,
        List<ExportDiagnostic> diagnostics)
    {
        var pkByTableAndVariant = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        for (int snapshotIndex = 0; snapshotIndex < snapshots.Count; snapshotIndex++)
        {
            ExportTableVariantSnapshot snapshot = snapshots[snapshotIndex];
            ExportTableModel tableModel = snapshot.ExportTable;
            ExportPrimaryKeyModel? primaryKey = tableModel.PrimaryKey;
            if (primaryKey == null)
            {
                continue;
            }

            var map = new Dictionary<string, int>(tableModel.Table.Rows.Count, StringComparer.Ordinal);
            for (int rowIndex = 0; rowIndex < tableModel.Table.Rows.Count; rowIndex++)
            {
                DocRow row = tableModel.Table.Rows[rowIndex];
                int key = primaryKey.GetRowKey(row, diagnostics, tableModel.Table.Id);
                map[row.Id] = key;
            }

            string tableVariantKey = CreateTableVariantKey(tableModel.Table.Id, snapshot.VariantId);
            pkByTableAndVariant[tableVariantKey] = map;
        }

        return pkByTableAndVariant;
    }

    private static Dictionary<string, uint> BuildStringRegistry(
        List<ExportTableVariantSnapshot> snapshots,
        List<ExportDiagnostic> diagnostics)
    {
        var strings = new HashSet<string>(StringComparer.Ordinal);

        for (int snapshotIndex = 0; snapshotIndex < snapshots.Count; snapshotIndex++)
        {
            ExportTableVariantSnapshot snapshot = snapshots[snapshotIndex];
            ExportTableModel tableModel = snapshot.ExportTable;
            for (int rowIndex = 0; rowIndex < tableModel.Table.Rows.Count; rowIndex++)
            {
                DocRow row = tableModel.Table.Rows[rowIndex];
                for (int columnIndex = 0; columnIndex < tableModel.Columns.Count; columnIndex++)
                {
                    ExportColumnModel column = tableModel.Columns[columnIndex];
                    if (column.FieldKind != ExportFieldKind.StringHandle &&
                        column.FieldKind != ExportFieldKind.SplineHandle)
                    {
                        continue;
                    }

                    string value = row.GetCell(column.SourceColumn).StringValue ?? "";
                    if (column.FieldKind == ExportFieldKind.SplineHandle)
                    {
                        value = NormalizeSplineValue(value);
                    }

                    if (!string.IsNullOrEmpty(value))
                    {
                        strings.Add(value);
                    }
                }
            }
        }

        var list = strings.ToList();
        list.Sort(StringComparer.Ordinal);

        var map = new Dictionary<string, uint>(list.Count, StringComparer.Ordinal);
        var valueById = new Dictionary<uint, string>();
        for (int i = 0; i < list.Count; i++)
        {
            string value = list[i];
            uint id = StringRegistry.ComputeStableId(value);

            if (valueById.TryGetValue(id, out var existing) && !string.Equals(existing, value, StringComparison.Ordinal))
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/strings/id-collision",
                    $"String ID collision for '{existing}' and '{value}' (id {id:X8}).",
                    TableId: null,
                    ColumnId: null));
                return map;
            }

            valueById[id] = value;
            map[value] = id;
        }

        return map;
    }

    private static void AddTableAndIndexes(
        DerpDocBinaryWriter writer,
        ExportTableModel tableModel,
        string binaryTableName,
        int variantId,
        Dictionary<string, Dictionary<string, int>> pkValueByTableAndVariant,
        Dictionary<string, uint> stringIdByValue,
        List<ExportDiagnostic> diagnostics)
    {
        var table = tableModel.Table;

        int recordSize = 0;
        for (int i = 0; i < tableModel.Columns.Count; i++)
        {
            recordSize = checked(recordSize + tableModel.Columns[i].FieldSizeBytes);
        }

        int rowCount = table.Rows.Count;
        var records = new byte[checked(rowCount * recordSize)];

        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            int offset = rowIndex * recordSize;
            var row = table.Rows[rowIndex];
            for (int colIndex = 0; colIndex < tableModel.Columns.Count; colIndex++)
            {
                var col = tableModel.Columns[colIndex];
                var cell = row.GetCell(col.SourceColumn);
                WriteField(tableModel, col, row, cell, variantId, pkValueByTableAndVariant, stringIdByValue, records, ref offset, diagnostics);
                if (HasError(diagnostics))
                {
                    return;
                }
            }
        }

        int[] slotArray = Array.Empty<int>();
        if (tableModel.PrimaryKey != null)
        {
            slotArray = BuildPrimaryKeySlotArray(tableModel, diagnostics);
            if (HasError(diagnostics))
            {
                return;
            }
        }

        writer.AddTable(new BinaryTableSection
        {
            Name = binaryTableName,
            RecordSize = recordSize,
            Records = records,
            RecordCount = checked((uint)rowCount),
            SlotArray = slotArray,
        });

        if (tableModel.PrimaryKey != null)
        {
            AddSortedKeyIndex(writer, binaryTableName + "__pk_sorted", tableModel, tableModel.PrimaryKey, diagnostics);
            if (HasError(diagnostics))
            {
                return;
            }
        }

        for (int i = 0; i < tableModel.SecondaryKeys.Count; i++)
        {
            var sk = tableModel.SecondaryKeys[i];
            string secondaryFieldName = CSharpIdentifier.Sanitize(CSharpIdentifier.ToPascalCase(sk.Column.Name), "Key");
            string secondaryIndexTableName = binaryTableName + "__sk_" + secondaryFieldName + (sk.Unique ? "__unique" : "__pairs");
            if (sk.Unique)
            {
                AddSecondaryUniqueIndex(writer, secondaryIndexTableName, tableModel, sk, diagnostics);
            }
            else
            {
                AddSecondaryNonUniqueIndex(writer, secondaryIndexTableName, tableModel, sk, diagnostics);
            }

            if (HasError(diagnostics))
            {
                return;
            }
        }

        if (tableModel.SubtableParent != null)
        {
            AddSubtableParentIndex(
                writer,
                tableModel,
                binaryTableName,
                variantId,
                pkValueByTableAndVariant,
                diagnostics);
            if (HasError(diagnostics))
            {
                return;
            }
        }

        if (tableModel.RowReferences.Count > 0)
        {
            AddRowReferenceIndexes(
                writer,
                tableModel,
                binaryTableName,
                variantId,
                pkValueByTableAndVariant,
                diagnostics);
            if (HasError(diagnostics))
            {
                return;
            }
        }
    }

    private static void WriteField(
        ExportTableModel tableModel,
        ExportColumnModel col,
        DocRow row,
        DocCellValue cell,
        int variantId,
        Dictionary<string, Dictionary<string, int>> pkValueByTableAndVariant,
        Dictionary<string, uint> stringIdByValue,
        byte[] recordBytes,
        ref int offset,
        List<ExportDiagnostic> diagnostics)
    {
        string columnTypeId = DocColumnTypeIdMapper.Resolve(col.SourceColumn.ColumnTypeId, col.SourceColumn.Kind);
        if (!DocColumnTypeIdMapper.IsBuiltIn(columnTypeId))
        {
            if (ColumnExportProviderRegistry.TryWriteField(
                    columnTypeId,
                    tableModel,
                    col,
                    row,
                    cell,
                    pkValueByTableAndVariant,
                    stringIdByValue,
                    recordBytes,
                    ref offset,
                    diagnostics))
            {
                return;
            }

            diagnostics.Add(new ExportDiagnostic(
                ExportDiagnosticSeverity.Error,
                "export/convert/missing-provider",
                $"No export provider writer is registered for column type id '{columnTypeId}' on '{tableModel.Table.Name}.{col.SourceColumn.Name}'.",
                TableId: tableModel.Table.Id,
                ColumnId: col.SourceColumn.Id));
            return;
        }

        switch (col.FieldKind)
        {
            case ExportFieldKind.Int32:
            {
                if (!TryConvertNumberToInt(cell.NumberValue, out int value))
                {
                    diagnostics.Add(new ExportDiagnostic(
                        ExportDiagnosticSeverity.Error,
                        "export/convert/int",
                        $"Expected integer value for '{tableModel.Table.Name}.{col.SourceColumn.Name}'.",
                        TableId: tableModel.Table.Id,
                        ColumnId: col.SourceColumn.Id));
                    value = 0;
                }

                BinaryPrimitives.WriteInt32LittleEndian(recordBytes.AsSpan(offset, 4), value);
                offset += 4;
                break;
            }
            case ExportFieldKind.Float32:
            {
                float value = (float)cell.NumberValue;
                BinaryPrimitives.WriteInt32LittleEndian(recordBytes.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));
                offset += 4;
                break;
            }
            case ExportFieldKind.Float64:
            {
                double value = cell.NumberValue;
                BinaryPrimitives.WriteInt64LittleEndian(recordBytes.AsSpan(offset, 8), BitConverter.DoubleToInt64Bits(value));
                offset += 8;
                break;
            }
            case ExportFieldKind.Fixed32:
            {
                int raw = Fixed32.FromDouble(cell.NumberValue).Raw;
                BinaryPrimitives.WriteInt32LittleEndian(recordBytes.AsSpan(offset, 4), raw);
                offset += 4;
                break;
            }
            case ExportFieldKind.Fixed64:
            {
                long raw = Fixed64.FromDouble(cell.NumberValue).Raw;
                BinaryPrimitives.WriteInt64LittleEndian(recordBytes.AsSpan(offset, 8), raw);
                offset += 8;
                break;
            }
            case ExportFieldKind.Byte:
            {
                recordBytes[offset] = cell.BoolValue ? (byte)1 : (byte)0;
                offset += 1;
                break;
            }
            case ExportFieldKind.StringHandle:
            case ExportFieldKind.SplineHandle:
            {
                string value = cell.StringValue ?? "";
                if (col.FieldKind == ExportFieldKind.SplineHandle)
                {
                    value = NormalizeSplineValue(value);
                }

                uint id = 0;
                if (!string.IsNullOrEmpty(value) && !stringIdByValue.TryGetValue(value, out id))
                {
                    diagnostics.Add(new ExportDiagnostic(
                        ExportDiagnosticSeverity.Error,
                        "export/string/registry-missing",
                        $"String registry missing value '{value}'.",
                        TableId: tableModel.Table.Id,
                        ColumnId: col.SourceColumn.Id));
                    id = 0;
                }

                BinaryPrimitives.WriteUInt32LittleEndian(recordBytes.AsSpan(offset, 4), id);
                offset += 4;
                break;
            }
            case ExportFieldKind.Enum:
            {
                int enumValue = col.EnumModel!.GetValue(cell.StringValue ?? "", diagnostics, tableModel.Table.Id, col.SourceColumn.Id);
                if (col.FieldSizeBytes == 1)
                {
                    recordBytes[offset] = checked((byte)enumValue);
                    offset += 1;
                }
                else
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(recordBytes.AsSpan(offset, 2), checked((ushort)enumValue));
                    offset += 2;
                }
                break;
            }
            case ExportFieldKind.ForeignKeyInt32:
            {
                int value = -1;
                string relationRowId = cell.StringValue ?? "";
                if (!string.IsNullOrEmpty(relationRowId))
                {
                    value = ResolveForeignKeyPk(
                        tableModel.Table,
                        col.SourceColumn,
                        relationRowId,
                        variantId,
                        pkValueByTableAndVariant,
                        diagnostics);
                }

                BinaryPrimitives.WriteInt32LittleEndian(recordBytes.AsSpan(offset, 4), value);
                offset += 4;
                break;
            }
            case ExportFieldKind.SubtableParentForeignKeyInt32:
            {
                int value = -1;
                string parentRowId = cell.StringValue ?? "";
                if (!string.IsNullOrEmpty(parentRowId))
                {
                    value = ResolveSubtableParentPk(
                        tableModel.Table,
                        col.SourceColumn,
                        parentRowId,
                        variantId,
                        pkValueByTableAndVariant,
                        diagnostics);
                }

                BinaryPrimitives.WriteInt32LittleEndian(recordBytes.AsSpan(offset, 4), value);
                offset += 4;
                break;
            }
            case ExportFieldKind.Fixed32Vec2:
            {
                WriteFixed32(recordBytes, ref offset, Fixed32.FromDouble(cell.XValue).Raw);
                WriteFixed32(recordBytes, ref offset, Fixed32.FromDouble(cell.YValue).Raw);
                break;
            }
            case ExportFieldKind.Fixed32Vec3:
            {
                WriteFixed32(recordBytes, ref offset, Fixed32.FromDouble(cell.XValue).Raw);
                WriteFixed32(recordBytes, ref offset, Fixed32.FromDouble(cell.YValue).Raw);
                WriteFixed32(recordBytes, ref offset, Fixed32.FromDouble(cell.ZValue).Raw);
                break;
            }
            case ExportFieldKind.Fixed32Vec4:
            {
                WriteFixed32(recordBytes, ref offset, Fixed32.FromDouble(cell.XValue).Raw);
                WriteFixed32(recordBytes, ref offset, Fixed32.FromDouble(cell.YValue).Raw);
                WriteFixed32(recordBytes, ref offset, Fixed32.FromDouble(cell.ZValue).Raw);
                WriteFixed32(recordBytes, ref offset, Fixed32.FromDouble(cell.WValue).Raw);
                break;
            }
            case ExportFieldKind.Fixed64Vec2:
            {
                WriteFixed64(recordBytes, ref offset, Fixed64.FromDouble(cell.XValue).Raw);
                WriteFixed64(recordBytes, ref offset, Fixed64.FromDouble(cell.YValue).Raw);
                break;
            }
            case ExportFieldKind.Fixed64Vec3:
            {
                WriteFixed64(recordBytes, ref offset, Fixed64.FromDouble(cell.XValue).Raw);
                WriteFixed64(recordBytes, ref offset, Fixed64.FromDouble(cell.YValue).Raw);
                WriteFixed64(recordBytes, ref offset, Fixed64.FromDouble(cell.ZValue).Raw);
                break;
            }
            case ExportFieldKind.Fixed64Vec4:
            {
                WriteFixed64(recordBytes, ref offset, Fixed64.FromDouble(cell.XValue).Raw);
                WriteFixed64(recordBytes, ref offset, Fixed64.FromDouble(cell.YValue).Raw);
                WriteFixed64(recordBytes, ref offset, Fixed64.FromDouble(cell.ZValue).Raw);
                WriteFixed64(recordBytes, ref offset, Fixed64.FromDouble(cell.WValue).Raw);
                break;
            }
            case ExportFieldKind.Color32:
            {
                recordBytes[offset] = ConvertColorChannelToByte(cell.XValue);
                recordBytes[offset + 1] = ConvertColorChannelToByte(cell.YValue);
                recordBytes[offset + 2] = ConvertColorChannelToByte(cell.ZValue);
                recordBytes[offset + 3] = ConvertColorChannelToByte(cell.WValue);
                offset += 4;
                break;
            }
            default:
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/convert/unsupported",
                    $"Unsupported export field kind: {col.FieldKind}.",
                    TableId: tableModel.Table.Id,
                    ColumnId: col.SourceColumn.Id));
                break;
            }
        }
    }

    private static int ResolveForeignKeyPk(
        DocTable currentTable,
        DocColumn relationColumn,
        string relationRowId,
        int variantId,
        Dictionary<string, Dictionary<string, int>> pkValueByTableAndVariant,
        List<ExportDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(relationColumn.RelationTableId))
        {
            diagnostics.Add(new ExportDiagnostic(
                ExportDiagnosticSeverity.Error,
                "export/fk/missing-target",
                $"Relation column '{currentTable.Name}.{relationColumn.Name}' has no RelationTableId.",
                TableId: currentTable.Id,
                ColumnId: relationColumn.Id));
            return -1;
        }

        int relationTargetVariantId = relationColumn.RelationTableVariantId;
        string variantTableKey = CreateTableVariantKey(relationColumn.RelationTableId, relationTargetVariantId);
        if (!pkValueByTableAndVariant.TryGetValue(variantTableKey, out var rowIdToPk))
        {
            diagnostics.Add(new ExportDiagnostic(
                ExportDiagnosticSeverity.Error,
                "export/fk/target-not-exported",
                $"Relation target table '{relationColumn.RelationTableId}' is not exported or has no primary key for variant '{relationTargetVariantId.ToString(CultureInfo.InvariantCulture)}'.",
                TableId: currentTable.Id,
                ColumnId: relationColumn.Id));
            return -1;
        }

        if (!rowIdToPk.TryGetValue(relationRowId, out int pk))
        {
            diagnostics.Add(new ExportDiagnostic(
                ExportDiagnosticSeverity.Error,
                "export/fk/unresolved",
                $"Foreign key row id '{relationRowId}' not found in target table '{relationColumn.RelationTableId}'.",
                TableId: currentTable.Id,
                ColumnId: relationColumn.Id));
            return -1;
        }

        return pk;
    }

    private static int ResolveSubtableParentPk(
        DocTable currentTable,
        DocColumn parentColumn,
        string parentRowId,
        int variantId,
        Dictionary<string, Dictionary<string, int>> pkValueByTableAndVariant,
        List<ExportDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(currentTable.ParentTableId))
        {
            diagnostics.Add(new ExportDiagnostic(
                ExportDiagnosticSeverity.Error,
                "export/subtable/missing-parent-table",
                $"Subtable '{currentTable.Name}' has no ParentTableId while exporting parent key column '{parentColumn.Name}'.",
                TableId: currentTable.Id,
                ColumnId: parentColumn.Id));
            return -1;
        }

        string variantTableKey = CreateTableVariantKey(currentTable.ParentTableId, variantId);
        if (!pkValueByTableAndVariant.TryGetValue(variantTableKey, out Dictionary<string, int>? parentPkByRowId))
        {
            diagnostics.Add(new ExportDiagnostic(
                ExportDiagnosticSeverity.Error,
                "export/subtable/parent-not-exported",
                $"Parent table '{currentTable.ParentTableId}' for subtable '{currentTable.Name}' is not exported for variant '{variantId.ToString(CultureInfo.InvariantCulture)}'.",
                TableId: currentTable.Id,
                ColumnId: parentColumn.Id));
            return -1;
        }

        if (!parentPkByRowId.TryGetValue(parentRowId, out int parentPk))
        {
            diagnostics.Add(new ExportDiagnostic(
                ExportDiagnosticSeverity.Error,
                "export/subtable/parent-row-unresolved",
                $"Subtable parent row id '{parentRowId}' not found in parent table '{currentTable.ParentTableId}'.",
                TableId: currentTable.Id,
                ColumnId: parentColumn.Id));
            return -1;
        }

        return parentPk;
    }

    private static void WriteFixed32(byte[] recordBytes, ref int offset, int raw)
    {
        BinaryPrimitives.WriteInt32LittleEndian(recordBytes.AsSpan(offset, 4), raw);
        offset += 4;
    }

    private static void WriteFixed64(byte[] recordBytes, ref int offset, long raw)
    {
        BinaryPrimitives.WriteInt64LittleEndian(recordBytes.AsSpan(offset, 8), raw);
        offset += 8;
    }

    private static byte ConvertColorChannelToByte(double value)
    {
        double clamped = Math.Clamp(value, 0.0, 1.0);
        int scaled = (int)Math.Round(clamped * 255.0, MidpointRounding.AwayFromZero);
        return (byte)Math.Clamp(scaled, 0, 255);
    }

    private static int[] BuildPrimaryKeySlotArray(ExportTableModel tableModel, List<ExportDiagnostic> diagnostics)
    {
        var pk = tableModel.PrimaryKey;
        if (pk == null)
        {
            return Array.Empty<int>();
        }

        var table = tableModel.Table;
        int max = -1;
        var seen = new Dictionary<int, string>(table.Rows.Count);

        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            int key = pk.GetRowKey(table.Rows[rowIndex], diagnostics, table.Id);
            if (key < 0)
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/key/pk-negative",
                    $"Primary key must be non-negative in table '{table.Name}'.",
                    TableId: table.Id,
                    ColumnId: pk.Column.Id));
                continue;
            }

            if (seen.TryGetValue(key, out var existingRowId))
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/key/pk-duplicate",
                    $"Duplicate primary key '{key}' in table '{table.Name}' (rows '{existingRowId}' and '{table.Rows[rowIndex].Id}').",
                    TableId: table.Id,
                    ColumnId: pk.Column.Id));
                continue;
            }

            seen[key] = table.Rows[rowIndex].Id;
            if (key > max)
            {
                max = key;
            }
        }

        if (HasError(diagnostics))
        {
            return Array.Empty<int>();
        }

        if (max < 0)
        {
            return Array.Empty<int>();
        }

        var slots = new int[max + 1];
        Array.Fill(slots, -1);

        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            int key = pk.GetRowKey(table.Rows[rowIndex], diagnostics, table.Id);
            if ((uint)key >= (uint)slots.Length)
            {
                continue;
            }
            slots[key] = rowIndex;
        }

        return slots;
    }

    private static void AddSortedKeyIndex(
        DerpDocBinaryWriter writer,
        string indexTableName,
        ExportTableModel tableModel,
        ExportPrimaryKeyModel primaryKey,
        List<ExportDiagnostic> diagnostics)
    {
        var pairs = BuildSortedPairs(tableModel, row => primaryKey.GetRowKey(row, diagnostics, tableModel.Table.Id));
        var bytes = PackPairs(pairs);

        writer.AddTable(new BinaryTableSection
        {
            Name = indexTableName,
            RecordSize = 8,
            Records = bytes,
            RecordCount = checked((uint)pairs.Length),
        });
    }

    private static void AddSecondaryUniqueIndex(
        DerpDocBinaryWriter writer,
        string indexTableName,
        ExportTableModel tableModel,
        ExportSecondaryKeyModel key,
        List<ExportDiagnostic> diagnostics)
    {
        var table = tableModel.Table;
        int max = -1;
        var seen = new Dictionary<int, string>(table.Rows.Count);

        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            int k = key.GetRowKey(table.Rows[rowIndex], diagnostics, table.Id);
            if (seen.TryGetValue(k, out var existingRowId))
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/key/secondary-duplicate",
                    $"Duplicate unique secondary key '{k}' for '{table.Name}.{key.Column.Name}' (rows '{existingRowId}' and '{table.Rows[rowIndex].Id}').",
                    TableId: table.Id,
                    ColumnId: key.Column.Id));
                continue;
            }

            seen[k] = table.Rows[rowIndex].Id;
            if (k > max)
            {
                max = k;
            }
        }

        if (HasError(diagnostics))
        {
            return;
        }

        if (max < 0)
        {
            writer.AddTable(new BinaryTableSection
            {
                Name = indexTableName,
                RecordSize = 4,
                Records = Array.Empty<byte>(),
                RecordCount = 0,
            });
            return;
        }

        var map = new int[max + 1];
        Array.Fill(map, -1);

        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            int k = key.GetRowKey(table.Rows[rowIndex], diagnostics, table.Id);
            if ((uint)k >= (uint)map.Length)
            {
                continue;
            }
            map[k] = rowIndex;
        }

        var bytes = new byte[map.Length * 4];
        for (int i = 0; i < map.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4, 4), map[i]);
        }

        writer.AddTable(new BinaryTableSection
        {
            Name = indexTableName,
            RecordSize = 4,
            Records = bytes,
            RecordCount = checked((uint)map.Length),
        });
    }

    private static void AddSecondaryNonUniqueIndex(
        DerpDocBinaryWriter writer,
        string indexTableName,
        ExportTableModel tableModel,
        ExportSecondaryKeyModel key,
        List<ExportDiagnostic> diagnostics)
    {
        var pairs = BuildSortedPairs(tableModel, row => key.GetRowKey(row, diagnostics, tableModel.Table.Id));
        var bytes = PackPairs(pairs);

        writer.AddTable(new BinaryTableSection
        {
            Name = indexTableName,
            RecordSize = 8,
            Records = bytes,
            RecordCount = checked((uint)pairs.Length),
        });
    }

    private static void AddSubtableParentIndex(
        DerpDocBinaryWriter writer,
        ExportTableModel childTableModel,
        string binaryTableName,
        int variantId,
        Dictionary<string, Dictionary<string, int>> pkValueByTableAndVariant,
        List<ExportDiagnostic> diagnostics)
    {
        ExportSubtableLinkModel parentLink = childTableModel.SubtableParent!;
        DocColumn parentRowColumn = parentLink.ChildParentRowColumn;
        DocTable childTable = childTableModel.Table;

        int rowCount = childTable.Rows.Count;
        var parentKeyByRowIndex = new int[rowCount];
        Array.Fill(parentKeyByRowIndex, -1);
        int maxParentKey = -1;

        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            DocRow row = childTable.Rows[rowIndex];
            string parentRowId = row.GetCell(parentRowColumn).StringValue ?? "";
            if (string.IsNullOrEmpty(parentRowId))
            {
                continue;
            }

            int parentKey = ResolveSubtableParentPk(
                childTable,
                parentRowColumn,
                parentRowId,
                variantId,
                pkValueByTableAndVariant,
                diagnostics);
            if (parentKey < 0)
            {
                continue;
            }

            parentKeyByRowIndex[rowIndex] = parentKey;
            if (parentKey > maxParentKey)
            {
                maxParentKey = parentKey;
            }
        }

        if (HasError(diagnostics))
        {
            return;
        }

        if (maxParentKey > 1_000_000)
        {
            diagnostics.Add(new ExportDiagnostic(
                ExportDiagnosticSeverity.Error,
                "export/subtable/parent-index-range-too-large",
                $"Subtable parent index range for '{childTable.Name}' is too large ({maxParentKey}). Use denser parent key values.",
                TableId: childTable.Id,
                ColumnId: parentRowColumn.Id));
            return;
        }

        if (maxParentKey < 0)
        {
            writer.AddTable(new BinaryTableSection
            {
                Name = binaryTableName + "__sub_parent_ranges",
                RecordSize = 8,
                Records = Array.Empty<byte>(),
                RecordCount = 0,
            });
            writer.AddTable(new BinaryTableSection
            {
                Name = binaryTableName + "__sub_parent_rows",
                RecordSize = 4,
                Records = Array.Empty<byte>(),
                RecordCount = 0,
            });
            return;
        }

        var counts = new int[maxParentKey + 1];
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            int parentKey = parentKeyByRowIndex[rowIndex];
            if ((uint)parentKey >= (uint)counts.Length)
            {
                continue;
            }

            counts[parentKey]++;
        }

        var starts = new int[counts.Length];
        int running = 0;
        for (int parentKey = 0; parentKey < counts.Length; parentKey++)
        {
            starts[parentKey] = running;
            running += counts[parentKey];
        }

        var rowIndices = new int[running];
        var cursor = new int[starts.Length];
        Array.Copy(starts, cursor, starts.Length);
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            int parentKey = parentKeyByRowIndex[rowIndex];
            if ((uint)parentKey >= (uint)cursor.Length)
            {
                continue;
            }

            int destination = cursor[parentKey];
            cursor[parentKey] = destination + 1;
            rowIndices[destination] = rowIndex;
        }

        var rangeBytes = new byte[starts.Length * 8];
        for (int parentKey = 0; parentKey < starts.Length; parentKey++)
        {
            int offset = parentKey * 8;
            BinaryPrimitives.WriteInt32LittleEndian(rangeBytes.AsSpan(offset, 4), starts[parentKey]);
            BinaryPrimitives.WriteInt32LittleEndian(rangeBytes.AsSpan(offset + 4, 4), counts[parentKey]);
        }

        var rowBytes = new byte[rowIndices.Length * 4];
        for (int rowIndex = 0; rowIndex < rowIndices.Length; rowIndex++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(rowBytes.AsSpan(rowIndex * 4, 4), rowIndices[rowIndex]);
        }

        writer.AddTable(new BinaryTableSection
        {
            Name = binaryTableName + "__sub_parent_ranges",
            RecordSize = 8,
            Records = rangeBytes,
            RecordCount = checked((uint)starts.Length),
        });
        writer.AddTable(new BinaryTableSection
        {
            Name = binaryTableName + "__sub_parent_rows",
            RecordSize = 4,
            Records = rowBytes,
            RecordCount = checked((uint)rowIndices.Length),
        });
    }

    private static void AddRowReferenceIndexes(
        DerpDocBinaryWriter writer,
        ExportTableModel tableModel,
        string binaryTableName,
        int variantId,
        Dictionary<string, Dictionary<string, int>> pkValueByTableAndVariant,
        List<ExportDiagnostic> diagnostics)
    {
        for (int rowRefIndex = 0; rowRefIndex < tableModel.RowReferences.Count; rowRefIndex++)
        {
            ExportRowReferenceModel rowRef = tableModel.RowReferences[rowRefIndex];
            AddRowReferenceIndexesForModel(
                writer,
                tableModel,
                rowRef,
                binaryTableName,
                variantId,
                pkValueByTableAndVariant,
                diagnostics);
            if (HasError(diagnostics))
            {
                return;
            }
        }
    }

    private static void AddRowReferenceIndexesForModel(
        DerpDocBinaryWriter writer,
        ExportTableModel tableModel,
        ExportRowReferenceModel rowRef,
        string binaryTableName,
        int variantId,
        Dictionary<string, Dictionary<string, int>> pkValueByTableAndVariant,
        List<ExportDiagnostic> diagnostics)
    {
        if (tableModel.SubtableParent == null)
        {
            diagnostics.Add(new ExportDiagnostic(
                ExportDiagnosticSeverity.Error,
                "export/rowref/non-subtable-not-supported",
                $"Row-reference column '{tableModel.Table.Name}.{rowRef.RowIdColumn.Name}' currently requires a subtable parent scope.",
                TableId: tableModel.Table.Id,
                ColumnId: rowRef.RowIdColumn.Id));
            return;
        }

        int rowCount = tableModel.Table.Rows.Count;
        var rowTagByRowIndex = new int[rowCount];
        var rowTargetPkByRowIndex = new int[rowCount];
        var parentKeyByRowIndex = new int[rowCount];
        Array.Fill(rowTagByRowIndex, -1);
        Array.Fill(rowTargetPkByRowIndex, -1);
        Array.Fill(parentKeyByRowIndex, -1);

        var targetByTableId = new Dictionary<string, ExportRowReferenceTargetModel>(rowRef.Targets.Count, StringComparer.Ordinal);
        for (int targetIndex = 0; targetIndex < rowRef.Targets.Count; targetIndex++)
        {
            ExportRowReferenceTargetModel target = rowRef.Targets[targetIndex];
            targetByTableId[target.TargetTable.Table.Id] = target;
        }

        ExportSubtableLinkModel parentLink = tableModel.SubtableParent;
        int maxParentKey = -1;
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            DocRow row = tableModel.Table.Rows[rowIndex];

            string parentRowId = row.GetCell(parentLink.ChildParentRowColumn).StringValue ?? "";
            if (!string.IsNullOrEmpty(parentRowId))
            {
                int parentKey = ResolveSubtableParentPk(
                    tableModel.Table,
                    parentLink.ChildParentRowColumn,
                    parentRowId,
                    variantId,
                    pkValueByTableAndVariant,
                    diagnostics);
                if (parentKey >= 0)
                {
                    parentKeyByRowIndex[rowIndex] = parentKey;
                    if (parentKey > maxParentKey)
                    {
                        maxParentKey = parentKey;
                    }
                }
            }

            string targetTableId = row.GetCell(rowRef.TableRefColumn).StringValue ?? "";
            string targetRowId = row.GetCell(rowRef.RowIdColumn).StringValue ?? "";
            if (string.IsNullOrWhiteSpace(targetTableId) || string.IsNullOrWhiteSpace(targetRowId))
            {
                continue;
            }

            if (!targetByTableId.TryGetValue(targetTableId, out ExportRowReferenceTargetModel? targetModel))
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/rowref/target-table-not-exported",
                    $"Row-reference target table '{targetTableId}' for '{tableModel.Table.Name}.{rowRef.RowIdColumn.Name}' is not exported or not compatible with the configured TableRef base.",
                    TableId: tableModel.Table.Id,
                    ColumnId: rowRef.RowIdColumn.Id));
                continue;
            }

            string variantTableKey = CreateTableVariantKey(targetModel.TargetTable.Table.Id, variantId);
            if (!pkValueByTableAndVariant.TryGetValue(variantTableKey, out Dictionary<string, int>? targetPkByRowId))
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/rowref/target-missing-primary-key",
                    $"Row-reference target table '{targetModel.TargetTable.Table.Name}' has no exported primary key for variant '{variantId.ToString(CultureInfo.InvariantCulture)}'.",
                    TableId: tableModel.Table.Id,
                    ColumnId: rowRef.RowIdColumn.Id));
                continue;
            }

            if (!targetPkByRowId.TryGetValue(targetRowId, out int targetPk))
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/rowref/target-row-unresolved",
                    $"Row-reference target row id '{targetRowId}' was not found in table '{targetModel.TargetTable.Table.Name}'.",
                    TableId: tableModel.Table.Id,
                    ColumnId: rowRef.RowIdColumn.Id));
                continue;
            }

            rowTagByRowIndex[rowIndex] = targetModel.Tag;
            rowTargetPkByRowIndex[rowIndex] = targetPk;
        }

        if (HasError(diagnostics))
        {
            return;
        }

        writer.AddTable(new BinaryTableSection
        {
            Name = binaryTableName + rowRef.RowTargetsSuffix,
            RecordSize = 8,
            Records = PackTagPkPairs(rowTagByRowIndex, rowTargetPkByRowIndex),
            RecordCount = checked((uint)rowCount),
        });

        int kindCount = rowRef.Targets.Count;
        if (kindCount <= 0 || maxParentKey < 0)
        {
            AddEmptyRowReferenceIndexTables(writer, binaryTableName, rowRef);
            return;
        }

        if (maxParentKey > 1_000_000)
        {
            diagnostics.Add(new ExportDiagnostic(
                ExportDiagnosticSeverity.Error,
                "export/rowref/parent-range-too-large",
                $"Row-reference parent index range for '{tableModel.Table.Name}.{rowRef.RowIdColumn.Name}' is too large ({maxParentKey}). Use denser parent keys.",
                TableId: tableModel.Table.Id,
                ColumnId: rowRef.RowIdColumn.Id));
            return;
        }

        int parentKindSlotCount = checked((maxParentKey + 1) * kindCount);
        var parentKindCounts = new int[parentKindSlotCount];
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            int parentKey = parentKeyByRowIndex[rowIndex];
            int tag = rowTagByRowIndex[rowIndex];
            if (parentKey < 0 || tag <= 0)
            {
                continue;
            }

            int tagIndex = tag - 1;
            if ((uint)tagIndex >= (uint)kindCount)
            {
                continue;
            }

            int slot = checked(parentKey * kindCount + tagIndex);
            parentKindCounts[slot]++;
        }

        var parentKindStarts = new int[parentKindSlotCount];
        int parentKindRowTotal = 0;
        for (int slotIndex = 0; slotIndex < parentKindSlotCount; slotIndex++)
        {
            parentKindStarts[slotIndex] = parentKindRowTotal;
            parentKindRowTotal += parentKindCounts[slotIndex];
        }

        var parentKindRows = new int[parentKindRowTotal];
        var parentKindCursor = new int[parentKindSlotCount];
        Array.Copy(parentKindStarts, parentKindCursor, parentKindSlotCount);
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            int parentKey = parentKeyByRowIndex[rowIndex];
            int tag = rowTagByRowIndex[rowIndex];
            if (parentKey < 0 || tag <= 0)
            {
                continue;
            }

            int tagIndex = tag - 1;
            if ((uint)tagIndex >= (uint)kindCount)
            {
                continue;
            }

            int slot = checked(parentKey * kindCount + tagIndex);
            int destination = parentKindCursor[slot];
            parentKindCursor[slot] = destination + 1;
            parentKindRows[destination] = rowIndex;
        }

        writer.AddTable(new BinaryTableSection
        {
            Name = binaryTableName + rowRef.ParentKindRangesSuffix,
            RecordSize = 8,
            Records = PackRangePairs(parentKindStarts, parentKindCounts),
            RecordCount = checked((uint)parentKindStarts.Length),
        });
        writer.AddTable(new BinaryTableSection
        {
            Name = binaryTableName + rowRef.ParentKindRowsSuffix,
            RecordSize = 4,
            Records = PackInt32Array(parentKindRows),
            RecordCount = checked((uint)parentKindRows.Length),
        });

        BuildParentKindTargetIndexes(
            writer,
            tableModel,
            rowRef,
            binaryTableName,
            parentKindStarts,
            parentKindCounts,
            parentKindRows,
            rowTargetPkByRowIndex,
            diagnostics);
        if (HasError(diagnostics))
        {
            return;
        }

        BuildParentTargetIndexes(
            writer,
            tableModel,
            rowRef,
            binaryTableName,
            maxParentKey,
            parentKeyByRowIndex,
            rowTagByRowIndex,
            rowTargetPkByRowIndex,
            diagnostics);
    }

    private static void BuildParentKindTargetIndexes(
        DerpDocBinaryWriter writer,
        ExportTableModel tableModel,
        ExportRowReferenceModel rowRef,
        string binaryTableName,
        int[] parentKindStarts,
        int[] parentKindCounts,
        int[] parentKindRows,
        int[] rowTargetPkByRowIndex,
        List<ExportDiagnostic> diagnostics)
    {
        int slotCount = parentKindStarts.Length;
        var slotMetaOffset = new int[slotCount];
        var slotMetaLength = new int[slotCount];
        var targetRanges = new List<RangeValue>(Math.Max(8, parentKindRows.Length));
        var parentKindTargetRows = new int[parentKindRows.Length];
        int targetRowsWriteOffset = 0;

        for (int slot = 0; slot < slotCount; slot++)
        {
            int slotRowCount = parentKindCounts[slot];
            slotMetaOffset[slot] = targetRanges.Count;
            if (slotRowCount <= 0)
            {
                slotMetaLength[slot] = 0;
                continue;
            }

            int slotStart = parentKindStarts[slot];
            int maxTargetPk = -1;
            for (int rowOffset = 0; rowOffset < slotRowCount; rowOffset++)
            {
                int rowIndex = parentKindRows[slotStart + rowOffset];
                int targetPk = rowTargetPkByRowIndex[rowIndex];
                if (targetPk > maxTargetPk)
                {
                    maxTargetPk = targetPk;
                }
            }

            if (maxTargetPk > 1_000_000)
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/rowref/target-range-too-large",
                    $"Row-reference target range for '{tableModel.Table.Name}.{rowRef.RowIdColumn.Name}' is too large ({maxTargetPk}). Use denser target primary keys.",
                    TableId: tableModel.Table.Id,
                    ColumnId: rowRef.RowIdColumn.Id));
                return;
            }

            if (maxTargetPk < 0)
            {
                slotMetaLength[slot] = 0;
                continue;
            }

            int mapLength = maxTargetPk + 1;
            slotMetaLength[slot] = mapLength;
            for (int mapIndex = 0; mapIndex < mapLength; mapIndex++)
            {
                targetRanges.Add(default);
            }

            var localCounts = new int[mapLength];
            for (int rowOffset = 0; rowOffset < slotRowCount; rowOffset++)
            {
                int rowIndex = parentKindRows[slotStart + rowOffset];
                int targetPk = rowTargetPkByRowIndex[rowIndex];
                if ((uint)targetPk >= (uint)localCounts.Length)
                {
                    continue;
                }

                localCounts[targetPk]++;
            }

            var localStarts = new int[mapLength];
            int localRunning = 0;
            for (int targetPk = 0; targetPk < mapLength; targetPk++)
            {
                localStarts[targetPk] = localRunning;
                localRunning += localCounts[targetPk];
            }

            int baseTargetRowsStart = targetRowsWriteOffset;
            targetRowsWriteOffset += slotRowCount;
            var localCursor = new int[mapLength];
            Array.Copy(localStarts, localCursor, mapLength);

            for (int rowOffset = 0; rowOffset < slotRowCount; rowOffset++)
            {
                int rowIndex = parentKindRows[slotStart + rowOffset];
                int targetPk = rowTargetPkByRowIndex[rowIndex];
                if ((uint)targetPk >= (uint)localCursor.Length)
                {
                    continue;
                }

                int destination = baseTargetRowsStart + localCursor[targetPk];
                localCursor[targetPk]++;
                parentKindTargetRows[destination] = rowIndex;
            }

            int rangeOffset = slotMetaOffset[slot];
            for (int targetPk = 0; targetPk < mapLength; targetPk++)
            {
                int targetCount = localCounts[targetPk];
                if (targetCount <= 0)
                {
                    continue;
                }

                targetRanges[rangeOffset + targetPk] = new RangeValue(
                    baseTargetRowsStart + localStarts[targetPk],
                    targetCount);
            }
        }

        writer.AddTable(new BinaryTableSection
        {
            Name = binaryTableName + rowRef.ParentKindTargetMetaSuffix,
            RecordSize = 8,
            Records = PackRangePairs(slotMetaOffset, slotMetaLength),
            RecordCount = checked((uint)slotMetaOffset.Length),
        });
        writer.AddTable(new BinaryTableSection
        {
            Name = binaryTableName + rowRef.ParentKindTargetRangesSuffix,
            RecordSize = 8,
            Records = PackRangeValues(targetRanges),
            RecordCount = checked((uint)targetRanges.Count),
        });
        writer.AddTable(new BinaryTableSection
        {
            Name = binaryTableName + rowRef.ParentKindTargetRowsSuffix,
            RecordSize = 4,
            Records = PackInt32Array(parentKindTargetRows, targetRowsWriteOffset),
            RecordCount = checked((uint)targetRowsWriteOffset),
        });
    }

    private static void BuildParentTargetIndexes(
        DerpDocBinaryWriter writer,
        ExportTableModel tableModel,
        ExportRowReferenceModel rowRef,
        string binaryTableName,
        int maxParentKey,
        int[] parentKeyByRowIndex,
        int[] rowTagByRowIndex,
        int[] rowTargetPkByRowIndex,
        List<ExportDiagnostic> diagnostics)
    {
        int parentCount = maxParentKey + 1;
        var parentCounts = new int[parentCount];
        int rowCount = parentKeyByRowIndex.Length;
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            int parentKey = parentKeyByRowIndex[rowIndex];
            if (parentKey < 0 || rowTagByRowIndex[rowIndex] <= 0)
            {
                continue;
            }

            parentCounts[parentKey]++;
        }

        var parentStarts = new int[parentCount];
        int parentRowsTotal = 0;
        for (int parentKey = 0; parentKey < parentCount; parentKey++)
        {
            parentStarts[parentKey] = parentRowsTotal;
            parentRowsTotal += parentCounts[parentKey];
        }

        var parentRows = new int[parentRowsTotal];
        var parentCursor = new int[parentCount];
        Array.Copy(parentStarts, parentCursor, parentCount);
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            int parentKey = parentKeyByRowIndex[rowIndex];
            if (parentKey < 0 || rowTagByRowIndex[rowIndex] <= 0)
            {
                continue;
            }

            int destination = parentCursor[parentKey];
            parentCursor[parentKey] = destination + 1;
            parentRows[destination] = rowIndex;
        }

        var parentMetaOffset = new int[parentCount];
        var parentMetaLength = new int[parentCount];
        var targetRanges = new List<RangeValue>(Math.Max(8, parentRowsTotal));
        var parentTargetRows = new int[parentRows.Length];
        int targetRowsWriteOffset = 0;

        for (int parentKey = 0; parentKey < parentCount; parentKey++)
        {
            int bucketRowCount = parentCounts[parentKey];
            parentMetaOffset[parentKey] = targetRanges.Count;
            if (bucketRowCount <= 0)
            {
                parentMetaLength[parentKey] = 0;
                continue;
            }

            int bucketStart = parentStarts[parentKey];
            int maxTargetPk = -1;
            for (int rowOffset = 0; rowOffset < bucketRowCount; rowOffset++)
            {
                int rowIndex = parentRows[bucketStart + rowOffset];
                int targetPk = rowTargetPkByRowIndex[rowIndex];
                if (targetPk > maxTargetPk)
                {
                    maxTargetPk = targetPk;
                }
            }

            if (maxTargetPk > 1_000_000)
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/rowref/global-target-range-too-large",
                    $"Global row-reference target range for '{tableModel.Table.Name}.{rowRef.RowIdColumn.Name}' is too large ({maxTargetPk}). Use denser target primary keys.",
                    TableId: tableModel.Table.Id,
                    ColumnId: rowRef.RowIdColumn.Id));
                return;
            }

            if (maxTargetPk < 0)
            {
                parentMetaLength[parentKey] = 0;
                continue;
            }

            int mapLength = maxTargetPk + 1;
            parentMetaLength[parentKey] = mapLength;
            for (int mapIndex = 0; mapIndex < mapLength; mapIndex++)
            {
                targetRanges.Add(default);
            }

            var localCounts = new int[mapLength];
            for (int rowOffset = 0; rowOffset < bucketRowCount; rowOffset++)
            {
                int rowIndex = parentRows[bucketStart + rowOffset];
                int targetPk = rowTargetPkByRowIndex[rowIndex];
                if ((uint)targetPk >= (uint)localCounts.Length)
                {
                    continue;
                }

                localCounts[targetPk]++;
            }

            var localStarts = new int[mapLength];
            int localRunning = 0;
            for (int targetPk = 0; targetPk < mapLength; targetPk++)
            {
                localStarts[targetPk] = localRunning;
                localRunning += localCounts[targetPk];
            }

            int baseTargetRowsStart = targetRowsWriteOffset;
            targetRowsWriteOffset += bucketRowCount;
            var localCursor = new int[mapLength];
            Array.Copy(localStarts, localCursor, mapLength);
            for (int rowOffset = 0; rowOffset < bucketRowCount; rowOffset++)
            {
                int rowIndex = parentRows[bucketStart + rowOffset];
                int targetPk = rowTargetPkByRowIndex[rowIndex];
                if ((uint)targetPk >= (uint)localCursor.Length)
                {
                    continue;
                }

                int destination = baseTargetRowsStart + localCursor[targetPk];
                localCursor[targetPk]++;
                parentTargetRows[destination] = rowIndex;
            }

            int rangeOffset = parentMetaOffset[parentKey];
            for (int targetPk = 0; targetPk < mapLength; targetPk++)
            {
                int targetCount = localCounts[targetPk];
                if (targetCount <= 0)
                {
                    continue;
                }

                targetRanges[rangeOffset + targetPk] = new RangeValue(
                    baseTargetRowsStart + localStarts[targetPk],
                    targetCount);
            }
        }

        writer.AddTable(new BinaryTableSection
        {
            Name = binaryTableName + rowRef.ParentTargetMetaSuffix,
            RecordSize = 8,
            Records = PackRangePairs(parentMetaOffset, parentMetaLength),
            RecordCount = checked((uint)parentMetaOffset.Length),
        });
        writer.AddTable(new BinaryTableSection
        {
            Name = binaryTableName + rowRef.ParentTargetRangesSuffix,
            RecordSize = 8,
            Records = PackRangeValues(targetRanges),
            RecordCount = checked((uint)targetRanges.Count),
        });
        writer.AddTable(new BinaryTableSection
        {
            Name = binaryTableName + rowRef.ParentTargetRowsSuffix,
            RecordSize = 4,
            Records = PackInt32Array(parentTargetRows, targetRowsWriteOffset),
            RecordCount = checked((uint)targetRowsWriteOffset),
        });
    }

    private static void AddEmptyRowReferenceIndexTables(
        DerpDocBinaryWriter writer,
        string binaryTableName,
        ExportRowReferenceModel rowRef)
    {
        writer.AddTable(new BinaryTableSection
        {
            Name = binaryTableName + rowRef.ParentKindRangesSuffix,
            RecordSize = 8,
            Records = Array.Empty<byte>(),
            RecordCount = 0,
        });
        writer.AddTable(new BinaryTableSection
        {
            Name = binaryTableName + rowRef.ParentKindRowsSuffix,
            RecordSize = 4,
            Records = Array.Empty<byte>(),
            RecordCount = 0,
        });
        writer.AddTable(new BinaryTableSection
        {
            Name = binaryTableName + rowRef.ParentKindTargetMetaSuffix,
            RecordSize = 8,
            Records = Array.Empty<byte>(),
            RecordCount = 0,
        });
        writer.AddTable(new BinaryTableSection
        {
            Name = binaryTableName + rowRef.ParentKindTargetRangesSuffix,
            RecordSize = 8,
            Records = Array.Empty<byte>(),
            RecordCount = 0,
        });
        writer.AddTable(new BinaryTableSection
        {
            Name = binaryTableName + rowRef.ParentKindTargetRowsSuffix,
            RecordSize = 4,
            Records = Array.Empty<byte>(),
            RecordCount = 0,
        });
        writer.AddTable(new BinaryTableSection
        {
            Name = binaryTableName + rowRef.ParentTargetMetaSuffix,
            RecordSize = 8,
            Records = Array.Empty<byte>(),
            RecordCount = 0,
        });
        writer.AddTable(new BinaryTableSection
        {
            Name = binaryTableName + rowRef.ParentTargetRangesSuffix,
            RecordSize = 8,
            Records = Array.Empty<byte>(),
            RecordCount = 0,
        });
        writer.AddTable(new BinaryTableSection
        {
            Name = binaryTableName + rowRef.ParentTargetRowsSuffix,
            RecordSize = 4,
            Records = Array.Empty<byte>(),
            RecordCount = 0,
        });
    }

    private static byte[] PackTagPkPairs(int[] tags, int[] pks)
    {
        int count = Math.Min(tags.Length, pks.Length);
        var bytes = new byte[checked(count * 8)];
        for (int index = 0; index < count; index++)
        {
            int offset = index * 8;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), tags[index]);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 4, 4), pks[index]);
        }

        return bytes;
    }

    private static byte[] PackRangePairs(int[] starts, int[] counts)
    {
        int count = Math.Min(starts.Length, counts.Length);
        var bytes = new byte[checked(count * 8)];
        for (int index = 0; index < count; index++)
        {
            int offset = index * 8;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), starts[index]);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 4, 4), counts[index]);
        }

        return bytes;
    }

    private static byte[] PackRangeValues(List<RangeValue> values)
    {
        var bytes = new byte[checked(values.Count * 8)];
        for (int index = 0; index < values.Count; index++)
        {
            int offset = index * 8;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), values[index].Start);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 4, 4), values[index].Count);
        }

        return bytes;
    }

    private static byte[] PackInt32Array(int[] values)
    {
        return PackInt32Array(values, values.Length);
    }

    private static byte[] PackInt32Array(int[] values, int count)
    {
        int clampedCount = Math.Clamp(count, 0, values.Length);
        var bytes = new byte[checked(clampedCount * 4)];
        for (int index = 0; index < clampedCount; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4, 4), values[index]);
        }

        return bytes;
    }

    private static SortablePair[] BuildSortedPairs(ExportTableModel tableModel, Func<DocRow, int> getKey)
    {
        var rows = tableModel.Table.Rows;
        var pairs = new SortablePair[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            pairs[i] = new SortablePair(getKey(rows[i]), i);
        }

        Array.Sort(pairs, static (a, b) =>
        {
            int cmp = a.Key.CompareTo(b.Key);
            if (cmp != 0) return cmp;
            return a.RowIndex.CompareTo(b.RowIndex);
        });

        return pairs;
    }

    private static byte[] PackPairs(SortablePair[] pairs)
    {
        var bytes = new byte[pairs.Length * 8];
        for (int i = 0; i < pairs.Length; i++)
        {
            int off = i * 8;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(off, 4), pairs[i].Key);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(off + 4, 4), pairs[i].RowIndex);
        }
        return bytes;
    }

    private static string NormalizeSplineValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        SplineUtils.SplinePoint[] points = SplineUtils.Deserialize(value);
        if (points.Length <= 0)
        {
            return "";
        }

        return SplineUtils.Serialize(points);
    }

    private static bool HasError(List<ExportDiagnostic> diagnostics)
    {
        for (int i = 0; i < diagnostics.Count; i++)
        {
            if (diagnostics[i].Severity == ExportDiagnosticSeverity.Error)
            {
                return true;
            }
        }

        return false;
    }

    private readonly struct RangeValue
    {
        public RangeValue(int start, int count)
        {
            Start = start;
            Count = count;
        }

        public int Start { get; }
        public int Count { get; }
    }

    private readonly struct SortablePair
    {
        public SortablePair(int key, int rowIndex)
        {
            Key = key;
            RowIndex = rowIndex;
        }

        public int Key { get; }
        public int RowIndex { get; }
    }
}
