using System.Text.Json;
using Derp.Doc.Model;

namespace Derp.Doc.Plugins;

public interface IColumnCellCodecProvider
{
    string ColumnTypeId { get; }

    bool TrySerializeCell(DocColumn column, DocCellValue cellValue, out JsonElement serializedCellValue);

    bool TryDeserializeCell(DocColumn column, JsonElement serializedCellValue, out DocCellValue cellValue);

    bool TryReadMcpCellValue(DocColumn column, JsonElement toolValue, out DocCellValue cellValue);

    bool TryFormatMcpCellValue(DocColumn column, DocCellValue cellValue, out object? toolValue);
}
