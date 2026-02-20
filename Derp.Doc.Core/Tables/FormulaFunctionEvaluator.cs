namespace Derp.Doc.Tables;

public delegate FormulaValue FormulaFunctionEvaluator(
    ReadOnlySpan<FormulaValue> arguments,
    in FormulaFunctionContext context);
