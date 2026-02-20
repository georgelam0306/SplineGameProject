using Derp.Doc.Model;

namespace Derp.Doc.Export;

internal sealed class ExportTableModel
{
    public ExportTableModel(
        DocProject project,
        DocTable table,
        string ns,
        string structName,
        string binaryTableName,
        string dbPropertyName,
        List<ExportColumnModel> columns,
        List<ExportTableVariableModel> variables,
        List<ExportViewBindingModel> viewBindings)
    {
        Project = project;
        Table = table;
        Namespace = ns;
        StructName = structName;
        BinaryTableName = binaryTableName;
        DbPropertyName = dbPropertyName;
        Columns = columns;
        Variables = variables;
        ViewBindings = viewBindings;
    }

    public DocProject Project { get; }
    public DocTable Table { get; }
    public string Namespace { get; }
    public string StructName { get; }
    public string BinaryTableName { get; }
    public string DbPropertyName { get; }
    public List<ExportColumnModel> Columns { get; }
    public List<ExportTableVariableModel> Variables { get; }
    public List<ExportViewBindingModel> ViewBindings { get; }

    public ExportPrimaryKeyModel? PrimaryKey { get; private set; }
    public List<ExportSecondaryKeyModel> SecondaryKeys { get; } = new();
    public List<ExportSubtableLinkModel> SubtableChildren { get; } = new();
    public ExportSubtableLinkModel? SubtableParent { get; set; }
    public List<ExportRowReferenceModel> RowReferences { get; } = new();

    public void BindKeys(List<ExportDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(Table.Keys.PrimaryKeyColumnId))
        {
            diagnostics.Add(new ExportDiagnostic(
                ExportDiagnosticSeverity.Error,
                "export/keys/missing-primary",
                $"Primary key is required for export table '{Table.Name}'.",
                TableId: Table.Id));
            return;
        }

        var pkColumn = FindColumnById(Table.Keys.PrimaryKeyColumnId);
        if (pkColumn == null)
        {
            diagnostics.Add(new ExportDiagnostic(
                ExportDiagnosticSeverity.Error,
                "export/keys/invalid-primary",
                $"Primary key column id '{Table.Keys.PrimaryKeyColumnId}' not found in table '{Table.Name}'.",
                TableId: Table.Id));
            return;
        }

        PrimaryKey = ExportPrimaryKeyModel.TryCreate(this, pkColumn, diagnostics);
        if (PrimaryKey == null)
        {
            return;
        }

        SecondaryKeys.Clear();
        for (int i = 0; i < Table.Keys.SecondaryKeys.Count; i++)
        {
            var sk = Table.Keys.SecondaryKeys[i];
            var skColumn = FindColumnById(sk.ColumnId);
            if (skColumn == null)
            {
                diagnostics.Add(new ExportDiagnostic(
                    ExportDiagnosticSeverity.Error,
                    "export/keys/invalid-secondary",
                    $"Secondary key column id '{sk.ColumnId}' not found in table '{Table.Name}'.",
                    TableId: Table.Id));
                continue;
            }

            var skModel = ExportSecondaryKeyModel.TryCreate(this, skColumn, sk.Unique, diagnostics);
            if (skModel != null)
            {
                SecondaryKeys.Add(skModel);
            }
        }
    }

    private DocColumn? FindColumnById(string columnId)
    {
        for (int i = 0; i < Table.Columns.Count; i++)
        {
            if (string.Equals(Table.Columns[i].Id, columnId, StringComparison.Ordinal))
            {
                return Table.Columns[i];
            }
        }
        return null;
    }
}
