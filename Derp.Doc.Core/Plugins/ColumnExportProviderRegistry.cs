using Derp.Doc.Export;
using Derp.Doc.Model;

namespace Derp.Doc.Plugins;

internal static class ColumnExportProviderRegistry
{
    private static readonly Dictionary<string, IColumnExportProvider> ProvidersByTypeId =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(IColumnExportProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        string columnTypeId = NormalizeTypeId(provider.ColumnTypeId);
        if (columnTypeId.Length == 0)
        {
            throw new ArgumentException("ColumnTypeId must be non-empty.", nameof(provider));
        }

        ProvidersByTypeId[columnTypeId] = provider;
    }

    public static void Clear()
    {
        ProvidersByTypeId.Clear();
    }

    public static bool TryCreateExportColumnModel(
        string columnTypeId,
        DocTable table,
        string structName,
        DocColumn column,
        string fieldName,
        HashSet<string> keyColumns,
        List<ExportDiagnostic> diagnostics,
        out ExportColumnModel exportColumnModel)
    {
        exportColumnModel = default;
        if (!TryGetProvider(columnTypeId, out var provider))
        {
            return false;
        }

        return provider.TryCreateExportColumnModel(
            table,
            structName,
            column,
            fieldName,
            keyColumns,
            diagnostics,
            out exportColumnModel);
    }

    public static bool TryWriteField(
        string columnTypeId,
        ExportTableModel tableModel,
        ExportColumnModel columnModel,
        DocRow row,
        DocCellValue cell,
        Dictionary<string, Dictionary<string, int>> primaryKeyValueByTableId,
        Dictionary<string, uint> stringIdByValue,
        byte[] recordBytes,
        ref int offset,
        List<ExportDiagnostic> diagnostics)
    {
        if (!TryGetProvider(columnTypeId, out var provider))
        {
            return false;
        }

        return provider.TryWriteField(
            tableModel,
            columnModel,
            row,
            cell,
            primaryKeyValueByTableId,
            stringIdByValue,
            recordBytes,
            ref offset,
            diagnostics);
    }

    private static bool TryGetProvider(string columnTypeId, out IColumnExportProvider provider)
    {
        return ProvidersByTypeId.TryGetValue(NormalizeTypeId(columnTypeId), out provider!);
    }

    private static string NormalizeTypeId(string? columnTypeId)
    {
        return string.IsNullOrWhiteSpace(columnTypeId)
            ? string.Empty
            : columnTypeId.Trim();
    }
}
