using Derp.Doc.Model;

namespace Derp.Doc.Tables;

public readonly struct FormulaFunctionContext
{
    public FormulaFunctionContext(
        IFormulaContext formulaContext,
        DocDocument? currentDocument,
        DocTable? currentTable,
        DocRow? currentRow,
        DocTable? parentTable,
        DocRow? parentRow)
    {
        FormulaContext = formulaContext;
        CurrentDocument = currentDocument;
        CurrentTable = currentTable;
        CurrentRow = currentRow;
        ParentTable = parentTable;
        ParentRow = parentRow;
    }

    public IFormulaContext FormulaContext { get; }
    public DocDocument? CurrentDocument { get; }
    public DocTable? CurrentTable { get; }
    public DocRow? CurrentRow { get; }
    public DocTable? ParentTable { get; }
    public DocRow? ParentRow { get; }
}
