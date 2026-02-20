using Derp.Doc.Model;

namespace Derp.Doc.Tables;

internal sealed class ProjectFormulaContext : IFormulaContext
{
    private readonly struct TableVariableDefinition
    {
        public TableVariableDefinition(string expression, bool hasExpression)
        {
            Expression = expression;
            HasExpression = hasExpression;
        }

        public string Expression { get; }
        public bool HasExpression { get; }
    }

    private readonly struct DocumentVariableDefinition
    {
        public DocumentVariableDefinition(string expression, bool hasExpression)
        {
            Expression = expression;
            HasExpression = hasExpression;
        }

        public string Expression { get; }
        public bool HasExpression { get; }
    }

    private readonly Dictionary<string, DocTable> _tableByName;
    private readonly Dictionary<string, DocTable> _tableById;
    private readonly Dictionary<string, Dictionary<string, DocColumn>> _columnByNameByTableId;
    private readonly Dictionary<string, Dictionary<string, DocRow>> _rowByIdByTableId;
    private readonly Dictionary<string, Dictionary<string, int>> _rowIndexByIdByTableId;
    private readonly Dictionary<string, Dictionary<string, TableVariableDefinition>> _tableVariableByNameByTableId;
    private readonly Dictionary<string, DocDocument> _documentByAlias;
    private readonly Dictionary<string, DocDocument> _documentById;
    private readonly Dictionary<string, Dictionary<string, DocumentVariableDefinition>> _variableByNameByDocumentId;

    public ProjectFormulaContext(DocProject project)
    {
        _tableByName = new Dictionary<string, DocTable>(StringComparer.OrdinalIgnoreCase);
        _tableById = new Dictionary<string, DocTable>(StringComparer.Ordinal);
        _columnByNameByTableId = new Dictionary<string, Dictionary<string, DocColumn>>(StringComparer.Ordinal);
        _rowByIdByTableId = new Dictionary<string, Dictionary<string, DocRow>>(StringComparer.Ordinal);
        _rowIndexByIdByTableId = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        _tableVariableByNameByTableId = new Dictionary<string, Dictionary<string, TableVariableDefinition>>(StringComparer.Ordinal);
        _documentByAlias = new Dictionary<string, DocDocument>(StringComparer.OrdinalIgnoreCase);
        _documentById = new Dictionary<string, DocDocument>(StringComparer.Ordinal);
        _variableByNameByDocumentId = new Dictionary<string, Dictionary<string, DocumentVariableDefinition>>(StringComparer.Ordinal);

        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            var table = project.Tables[tableIndex];
            _tableById[table.Id] = table;
            if (!_tableByName.ContainsKey(table.Name))
            {
                _tableByName[table.Name] = table;
            }

            BuildTableIndexes(table);
        }

        BuildDocumentIndexes(project);
    }

    public bool TryGetTableByName(string tableName, out DocTable table)
    {
        return _tableByName.TryGetValue(tableName, out table!);
    }

    public bool TryGetTableById(string tableId, out DocTable table)
    {
        return _tableById.TryGetValue(tableId, out table!);
    }

    public bool TryGetColumnByName(DocTable table, string columnName, out DocColumn column)
    {
        if (_columnByNameByTableId.TryGetValue(table.Id, out var columnByName) &&
            columnByName.TryGetValue(columnName, out var foundColumn))
        {
            column = foundColumn;
            return true;
        }

        column = null!;
        return false;
    }

    public bool TryGetRowById(DocTable table, string rowId, out DocRow row)
    {
        if (_rowByIdByTableId.TryGetValue(table.Id, out var rowById) &&
            rowById.TryGetValue(rowId, out var foundRow))
        {
            row = foundRow;
            return true;
        }

        row = null!;
        return false;
    }

    public int GetRowIndexOneBased(DocTable table, DocRow row)
    {
        if (_rowIndexByIdByTableId.TryGetValue(table.Id, out var rowIndexById) &&
            rowIndexById.TryGetValue(row.Id, out int rowIndexOneBased))
        {
            return rowIndexOneBased;
        }

        return 0;
    }

    public void RefreshTableIndexes(DocTable table)
    {
        BuildTableIndexes(table);
    }

    public string GetRowDisplayLabel(DocTable table, DocRow row)
    {
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var column = table.Columns[columnIndex];
            if (column.Kind != DocColumnKind.Id &&
                column.Kind != DocColumnKind.Text &&
                column.Kind != DocColumnKind.Select &&
                column.Kind != DocColumnKind.TableRef &&
                column.Kind != DocColumnKind.TextureAsset &&
                column.Kind != DocColumnKind.MeshAsset &&
                column.Kind != DocColumnKind.AudioAsset &&
                column.Kind != DocColumnKind.UiAsset &&
                column.Kind != DocColumnKind.Formula)
            {
                continue;
            }

            var value = row.GetCell(column).StringValue;
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return row.Id;
    }

    public bool TryGetTableVariableExpression(string tableId, string variableName, out string expression, out bool hasExpression)
    {
        expression = "";
        hasExpression = false;
        if (_tableVariableByNameByTableId.TryGetValue(tableId, out var tableVariableByName) &&
            tableVariableByName.TryGetValue(variableName, out var tableVariableDefinition))
        {
            expression = tableVariableDefinition.Expression;
            hasExpression = tableVariableDefinition.HasExpression;
            return true;
        }

        return false;
    }

    public bool TryGetDocumentByAlias(string documentAlias, out DocDocument document)
    {
        return _documentByAlias.TryGetValue(documentAlias, out document!);
    }

    public bool TryGetDocumentById(string documentId, out DocDocument document)
    {
        return _documentById.TryGetValue(documentId, out document!);
    }

    public bool TryGetDocumentVariableExpression(string documentId, string variableName, out string expression, out bool hasExpression)
    {
        expression = "";
        hasExpression = false;
        if (_variableByNameByDocumentId.TryGetValue(documentId, out var variableByName) &&
            variableByName.TryGetValue(variableName, out var variableDefinition))
        {
            expression = variableDefinition.Expression;
            hasExpression = variableDefinition.HasExpression;
            return true;
        }

        return false;
    }

    private void BuildTableIndexes(DocTable table)
    {
        var columnByName = new Dictionary<string, DocColumn>(StringComparer.OrdinalIgnoreCase);
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var column = table.Columns[columnIndex];
            if (!columnByName.ContainsKey(column.Name))
            {
                columnByName[column.Name] = column;
            }
        }
        _columnByNameByTableId[table.Id] = columnByName;

        var rowById = new Dictionary<string, DocRow>(table.Rows.Count, StringComparer.Ordinal);
        var rowIndexById = new Dictionary<string, int>(table.Rows.Count, StringComparer.Ordinal);
        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            rowById[row.Id] = row;
            rowIndexById[row.Id] = rowIndex + 1;
        }
        _rowByIdByTableId[table.Id] = rowById;
        _rowIndexByIdByTableId[table.Id] = rowIndexById;

        var tableVariableByName = new Dictionary<string, TableVariableDefinition>(StringComparer.OrdinalIgnoreCase);
        for (int variableIndex = 0; variableIndex < table.Variables.Count; variableIndex++)
        {
            var tableVariable = table.Variables[variableIndex];
            if (string.IsNullOrWhiteSpace(tableVariable.Name))
            {
                continue;
            }

            if (!tableVariableByName.ContainsKey(tableVariable.Name))
            {
                bool hasExpression = !string.IsNullOrWhiteSpace(tableVariable.Expression);
                tableVariableByName[tableVariable.Name] =
                    new TableVariableDefinition(tableVariable.Expression ?? "", hasExpression);
            }
        }
        _tableVariableByNameByTableId[table.Id] = tableVariableByName;
    }

    private void BuildDocumentIndexes(DocProject project)
    {
        for (int documentIndex = 0; documentIndex < project.Documents.Count; documentIndex++)
        {
            var document = project.Documents[documentIndex];
            _documentById[document.Id] = document;

            string primaryAlias = DocumentFormulaSyntax.NormalizeDocumentAlias(document.FileName);
            if (!_documentByAlias.ContainsKey(primaryAlias))
            {
                _documentByAlias[primaryAlias] = document;
            }

            string secondaryAlias = DocumentFormulaSyntax.NormalizeDocumentAlias(document.Title);
            if (!_documentByAlias.ContainsKey(secondaryAlias))
            {
                _documentByAlias[secondaryAlias] = document;
            }

            var variableByName = new Dictionary<string, DocumentVariableDefinition>(StringComparer.OrdinalIgnoreCase);
            for (int blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
            {
                var block = document.Blocks[blockIndex];
                if (block.Type != DocBlockType.Variable)
                {
                    continue;
                }

                if (!DocumentFormulaSyntax.TryParseVariableDeclaration(
                        block.Text.PlainText,
                        out string variableName,
                        out bool hasExpression,
                        out string expression))
                {
                    continue;
                }

                if (!variableByName.ContainsKey(variableName))
                {
                    variableByName[variableName] = new DocumentVariableDefinition(expression, hasExpression);
                }
            }

            _variableByNameByDocumentId[document.Id] = variableByName;
        }
    }
}
