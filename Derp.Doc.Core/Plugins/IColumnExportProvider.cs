using Derp.Doc.Export;
using Derp.Doc.Model;

namespace Derp.Doc.Plugins;

internal interface IColumnExportProvider
{
    string ColumnTypeId { get; }

    bool TryCreateExportColumnModel(
        DocTable table,
        string structName,
        DocColumn column,
        string fieldName,
        HashSet<string> keyColumns,
        List<ExportDiagnostic> diagnostics,
        out ExportColumnModel exportColumnModel);

    bool TryWriteField(
        ExportTableModel tableModel,
        ExportColumnModel columnModel,
        DocRow row,
        DocCellValue cell,
        Dictionary<string, Dictionary<string, int>> primaryKeyValueByTableId,
        Dictionary<string, uint> stringIdByValue,
        byte[] recordBytes,
        ref int offset,
        List<ExportDiagnostic> diagnostics);
}
