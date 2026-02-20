using Derp.Doc.Model;

namespace Derp.Doc.Plugins;

public interface IColumnDefaultValueProvider
{
    string ColumnTypeId { get; }

    bool TryCreateDefaultValue(DocColumn column, out DocCellValue defaultValue);
}
