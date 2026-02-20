namespace Derp.Doc.Tables;

public static class FormulaFunctionRegistry
{
    private static readonly Dictionary<string, FormulaFunctionDefinition> DefinitionsByName =
        new(StringComparer.OrdinalIgnoreCase);

    public static int Count => DefinitionsByName.Count;

    public static void Register(FormulaFunctionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new ArgumentException("Formula function name must be non-empty.", nameof(definition));
        }

        DefinitionsByName[definition.Name.Trim()] = definition;
    }

    public static void Clear()
    {
        DefinitionsByName.Clear();
    }

    public static bool RequiresFirstArgumentTableDependency(string functionName)
    {
        if (!TryGet(functionName, out var definition))
        {
            return false;
        }

        return definition.TracksFirstArgumentTableDependency;
    }

    public static bool TryEvaluate(
        string functionName,
        ReadOnlySpan<FormulaValue> arguments,
        in FormulaFunctionContext context,
        out FormulaValue result)
    {
        result = FormulaValue.Null();
        if (!TryGet(functionName, out var definition))
        {
            return false;
        }

        result = definition.Evaluator(arguments, context);
        return true;
    }

    public static bool Contains(string functionName)
    {
        return TryGet(functionName, out _);
    }

    public static bool Contains(ReadOnlySpan<char> functionName)
    {
        if (functionName.IsEmpty)
        {
            return false;
        }

        foreach (var pair in DefinitionsByName)
        {
            if (functionName.Equals(pair.Key.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static void CopyRegisteredFunctionNames(List<string> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        foreach (var pair in DefinitionsByName)
        {
            destination.Add(pair.Key);
        }

        destination.Sort(StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryGet(string functionName, out FormulaFunctionDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(functionName))
        {
            definition = null!;
            return false;
        }

        if (DefinitionsByName.TryGetValue(functionName.Trim(), out var existingDefinition))
        {
            definition = existingDefinition;
            return true;
        }

        definition = null!;
        return false;
    }
}
