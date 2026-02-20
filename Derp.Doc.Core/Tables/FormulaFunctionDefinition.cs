namespace Derp.Doc.Tables;

public sealed class FormulaFunctionDefinition
{
    public required string Name { get; init; }
    public required FormulaFunctionEvaluator Evaluator { get; init; }
    public bool TracksFirstArgumentTableDependency { get; init; }
}
