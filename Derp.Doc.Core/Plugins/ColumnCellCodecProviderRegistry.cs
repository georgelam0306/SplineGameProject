using System.Text.Json;
using Derp.Doc.Model;

namespace Derp.Doc.Plugins;

public static class ColumnCellCodecProviderRegistry
{
    private static readonly Dictionary<string, IColumnCellCodecProvider> ProvidersByTypeId =
        new(StringComparer.OrdinalIgnoreCase);

    public static int Count => ProvidersByTypeId.Count;

    public static void Register(IColumnCellCodecProvider provider)
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

    public static bool TrySerializeCell(string columnTypeId, DocColumn column, DocCellValue cellValue, out JsonElement serializedCellValue)
    {
        serializedCellValue = default;
        if (!TryGetProvider(columnTypeId, out var provider))
        {
            return false;
        }

        return provider.TrySerializeCell(column, cellValue, out serializedCellValue);
    }

    public static bool TryDeserializeCell(string columnTypeId, DocColumn column, JsonElement serializedCellValue, out DocCellValue cellValue)
    {
        cellValue = default;
        if (!TryGetProvider(columnTypeId, out var provider))
        {
            return false;
        }

        return provider.TryDeserializeCell(column, serializedCellValue, out cellValue);
    }

    public static bool TryReadMcpCellValue(string columnTypeId, DocColumn column, JsonElement toolValue, out DocCellValue cellValue)
    {
        cellValue = default;
        if (!TryGetProvider(columnTypeId, out var provider))
        {
            return false;
        }

        return provider.TryReadMcpCellValue(column, toolValue, out cellValue);
    }

    public static bool TryFormatMcpCellValue(string columnTypeId, DocColumn column, DocCellValue cellValue, out object? toolValue)
    {
        toolValue = null;
        if (!TryGetProvider(columnTypeId, out var provider))
        {
            return false;
        }

        return provider.TryFormatMcpCellValue(column, cellValue, out toolValue);
    }

    private static bool TryGetProvider(string columnTypeId, out IColumnCellCodecProvider provider)
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
