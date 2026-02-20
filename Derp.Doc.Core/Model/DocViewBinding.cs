namespace Derp.Doc.Model;

public sealed class DocViewBinding
{
    public string VariableName { get; set; } = "";
    public string FormulaExpression { get; set; } = "";

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(VariableName) &&
        string.IsNullOrWhiteSpace(FormulaExpression);

    public DocViewBinding Clone()
    {
        return new DocViewBinding
        {
            VariableName = VariableName,
            FormulaExpression = FormulaExpression,
        };
    }
}
