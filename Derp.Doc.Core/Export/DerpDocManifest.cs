using Derp.Doc.Model;
using Derp.Doc.Plugins;
using System;
using System.Globalization;

namespace Derp.Doc.Export;

public sealed class DerpDocManifest
{
    public string Namespace { get; set; } = "";
    public string BinaryPath { get; set; } = "";
    public List<DerpDocManifestTable> Tables { get; set; } = new();
    public List<string> ColumnTypeIds { get; set; } = new();

    internal static DerpDocManifest Create(
        ExportPipelineOptions options,
        List<ExportTableModel> exportTables,
        List<DocExportPipeline.ExportTableVariantSnapshot> tableVariantSnapshots,
        List<ExportDiagnostic> diagnostics)
    {
        string ns = exportTables.Count > 0 ? exportTables[0].Namespace : "DerpDocDatabase";
        var manifest = new DerpDocManifest
        {
            Namespace = ns,
            BinaryPath = options.BinaryOutputPath,
        };

        if (HasError(diagnostics))
        {
            return manifest;
        }

        var snapshotByTableAndVariantKey = new Dictionary<string, DocExportPipeline.ExportTableVariantSnapshot>(StringComparer.Ordinal);
        for (int snapshotIndex = 0; snapshotIndex < tableVariantSnapshots.Count; snapshotIndex++)
        {
            var snapshot = tableVariantSnapshots[snapshotIndex];
            string key = CreateTableVariantKey(snapshot.ExportTable.Table.Id, snapshot.VariantId);
            snapshotByTableAndVariantKey[key] = snapshot;
        }

        for (int i = 0; i < exportTables.Count; i++)
        {
            var t = exportTables[i];
            uint recordSize = 0;
            for (int c = 0; c < t.Columns.Count; c++)
            {
                recordSize += (uint)t.Columns[c].FieldSizeBytes;
            }

            uint baseRowCount = (uint)Math.Max(0, t.Table.Rows.Count);
            var tableManifest = new DerpDocManifestTable
            {
                Name = t.BinaryTableName,
                RowCount = baseRowCount,
                RecordSize = recordSize,
            };

            tableManifest.Variants.Add(new DerpDocManifestTableVariant
            {
                Id = DocTableVariant.BaseVariantId,
                VariantName = DocTableVariant.BaseVariantName,
                TableName = t.BinaryTableName,
                RowCount = baseRowCount,
            });

            for (int variantIndex = 0; variantIndex < t.Table.Variants.Count; variantIndex++)
            {
                DocTableVariant variant = t.Table.Variants[variantIndex];
                if (variant.Id == DocTableVariant.BaseVariantId)
                {
                    continue;
                }

                string variantKey = CreateTableVariantKey(t.Table.Id, variant.Id);
                uint rowCount = 0;
                if (snapshotByTableAndVariantKey.TryGetValue(variantKey, out var variantSnapshot))
                {
                    rowCount = (uint)Math.Max(0, variantSnapshot.ExportTable.Table.Rows.Count);
                }

                tableManifest.Variants.Add(new DerpDocManifestTableVariant
                {
                    Id = variant.Id,
                    VariantName = variant.Name,
                    TableName = t.BinaryTableName + "@v" + variant.Id.ToString(CultureInfo.InvariantCulture),
                    RowCount = rowCount,
                });
            }

            manifest.Tables.Add(tableManifest);
        }

        var customColumnTypeIds = new HashSet<string>(StringComparer.Ordinal);
        for (int tableIndex = 0; tableIndex < exportTables.Count; tableIndex++)
        {
            var table = exportTables[tableIndex];
            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                string columnTypeId = DocColumnTypeIdMapper.Resolve(
                    table.Columns[columnIndex].SourceColumn.ColumnTypeId,
                    table.Columns[columnIndex].SourceColumn.Kind);
                if (!DocColumnTypeIdMapper.IsBuiltIn(columnTypeId))
                {
                    customColumnTypeIds.Add(columnTypeId);
                }
            }
        }

        if (customColumnTypeIds.Count > 0)
        {
            manifest.ColumnTypeIds = customColumnTypeIds.ToList();
            manifest.ColumnTypeIds.Sort(StringComparer.Ordinal);
        }

        return manifest;
    }

    private static string CreateTableVariantKey(string tableId, int variantId)
    {
        return tableId + "|v" + variantId.ToString(CultureInfo.InvariantCulture);
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
}

public sealed class DerpDocManifestTable
{
    public string Name { get; set; } = "";
    public uint RowCount { get; set; }
    public uint RecordSize { get; set; }
    public List<DerpDocManifestTableVariant> Variants { get; set; } = new();
}

public sealed class DerpDocManifestTableVariant
{
    public int Id { get; set; }
    public string VariantName { get; set; } = "";
    public string TableName { get; set; } = "";
    public uint RowCount { get; set; }
}
