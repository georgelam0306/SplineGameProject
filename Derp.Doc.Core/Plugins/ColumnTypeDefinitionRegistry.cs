namespace Derp.Doc.Plugins;

public static class ColumnTypeDefinitionRegistry
{
    private static readonly Dictionary<string, ColumnTypeDefinition> DefinitionsByTypeId =
        new(StringComparer.OrdinalIgnoreCase);

    public static int Count => DefinitionsByTypeId.Count;

    public static void Register(ColumnTypeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        string columnTypeId = NormalizeTypeId(definition.ColumnTypeId);
        if (columnTypeId.Length == 0)
        {
            throw new ArgumentException("ColumnTypeId must be non-empty.", nameof(definition));
        }

        if (string.IsNullOrWhiteSpace(definition.DisplayName))
        {
            throw new ArgumentException("DisplayName must be non-empty.", nameof(definition));
        }

        var normalizedDefinition = new ColumnTypeDefinition
        {
            ColumnTypeId = columnTypeId,
            DisplayName = definition.DisplayName.Trim(),
            IconGlyph = string.IsNullOrWhiteSpace(definition.IconGlyph) ? null : definition.IconGlyph.Trim(),
            FallbackKind = definition.FallbackKind,
            IsTextWrappedByDefault = definition.IsTextWrappedByDefault,
            MinimumRowHeight = definition.MinimumRowHeight,
        };

        DefinitionsByTypeId[columnTypeId] = normalizedDefinition;
    }

    public static void Clear()
    {
        DefinitionsByTypeId.Clear();
    }

    public static bool TryGet(string? columnTypeId, out ColumnTypeDefinition definition)
    {
        return DefinitionsByTypeId.TryGetValue(NormalizeTypeId(columnTypeId), out definition!);
    }

    public static void CopyDefinitions(List<ColumnTypeDefinition> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();

        foreach (var pair in DefinitionsByTypeId)
        {
            destination.Add(pair.Value);
        }

        destination.Sort(static (left, right) =>
            string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeTypeId(string? columnTypeId)
    {
        return string.IsNullOrWhiteSpace(columnTypeId)
            ? string.Empty
            : columnTypeId.Trim();
    }
}
