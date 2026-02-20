using System;

namespace DerpLib.Ecs.Editor;

/// <summary>
/// A complete binding definition: sources + target + compiled expression.
/// </summary>
public sealed class ExpressionBindingDefinition
{
    public ExpressionBindingSource[] Sources = Array.Empty<ExpressionBindingSource>();
    public ExpressionBindingPath Target;
    public string ExpressionText = string.Empty;

    // Compiled representation (populated after parse + type-check + flatten)
    public ExpressionNode[] AstNodes = Array.Empty<ExpressionNode>();
    public int AstRootIndex = -1;
    public FlatExpression FlatExpression;
    public bool IsValid;
    public ExpressionDiagnostic[] Errors = Array.Empty<ExpressionDiagnostic>();

    /// <summary>
    /// Compiles the expression text into a flat expression ready for evaluation.
    /// </summary>
    public void Compile()
    {
        IsValid = false;

        if (string.IsNullOrEmpty(ExpressionText))
        {
            Errors = new[] { new ExpressionDiagnostic(0, 0, "Empty expression") };
            return;
        }

        // Build variable map from sources
        var variableMap = new System.Collections.Generic.Dictionary<string, int>(Sources.Length);
        Span<Property.PropertyKind> sourceKinds = stackalloc Property.PropertyKind[Sources.Length];

        for (int i = 0; i < Sources.Length; i++)
        {
            string alias = Sources[i].Alias;
            variableMap[alias] = i;
            sourceKinds[i] = Sources[i].Path.ValueKind;
        }

        // Parse
        var parser = new ExpressionParser(ExpressionText.AsMemory(), variableMap);
        var parseResult = parser.Parse();
        AstNodes = parseResult.Nodes;
        AstRootIndex = parseResult.RootIndex;

        if (parseResult.HasErrors)
        {
            Errors = parseResult.Diagnostics;
            return;
        }

        // Type check
        var typeErrors = ExpressionTypeChecker.Check(AstNodes.AsSpan(), sourceKinds);
        if (typeErrors.Length > 0)
        {
            Errors = typeErrors;
            return;
        }

        // Flatten
        FlatExpression = ExpressionFlattener.Flatten(AstNodes.AsSpan(), AstRootIndex);
        IsValid = FlatExpression.IsValid;
        Errors = Array.Empty<ExpressionDiagnostic>();
    }
}
