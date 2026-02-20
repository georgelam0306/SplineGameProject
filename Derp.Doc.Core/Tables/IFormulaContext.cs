using Derp.Doc.Model;

namespace Derp.Doc.Tables;

public interface IFormulaContext
{
    bool TryGetTableByName(string tableName, out DocTable table);
    bool TryGetTableById(string tableId, out DocTable table);
    bool TryGetColumnByName(DocTable table, string columnName, out DocColumn column);
    bool TryGetRowById(DocTable table, string rowId, out DocRow row);
    int GetRowIndexOneBased(DocTable table, DocRow row);
    void RefreshTableIndexes(DocTable table);
    string GetRowDisplayLabel(DocTable table, DocRow row);
    bool TryGetTableVariableExpression(string tableId, string variableName, out string expression, out bool hasExpression);
    bool TryGetDocumentByAlias(string documentAlias, out DocDocument document);
    bool TryGetDocumentById(string documentId, out DocDocument document);
    bool TryGetDocumentVariableExpression(string documentId, string variableName, out string expression, out bool hasExpression);
}
