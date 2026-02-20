using Derp.Doc.Model;

namespace Derp.Doc.Plugins;

public static class ColumnDefaultValueProviderRegistry
{
    private static readonly Dictionary<string, IColumnDefaultValueProvider> ProvidersByTypeId =
        new(StringComparer.OrdinalIgnoreCase);

    public static int Count => ProvidersByTypeId.Count;

    public static void Register(IColumnDefaultValueProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        string columnTypeId = NormalizeTypeId(provider.ColumnTypeId);
        if (columnTypeId.Length == 0)
        {
            throw new ArgumentException("ColumnTypeId must be non-empty.", nameof(provider));
        }

        ProvidersByTypeId[columnTypeId] = provider;
    }

    public static void Clear()
    {
        ProvidersByTypeId.Clear();
    }

    public static bool TryCreateDefaultValue(string columnTypeId, DocColumn column, out DocCellValue defaultValue)
    {
        defaultValue = default;
        if (!TryGetProvider(columnTypeId, out var provider))
        {
            return false;
        }

        return provider.TryCreateDefaultValue(column, out defaultValue);
    }

    private static bool TryGetProvider(string columnTypeId, out IColumnDefaultValueProvider provider)
    {
        return ProvidersByTypeId.TryGetValue(NormalizeTypeId(columnTypeId), out provider!);
    }

    private static string NormalizeTypeId(string? columnTypeId)
    {
        return string.IsNullOrWhiteSpace(columnTypeId)
            ? string.Empty
            : columnTypeId.Trim();
    }
}
