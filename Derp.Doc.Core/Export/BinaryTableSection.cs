namespace Derp.Doc.Export;

internal sealed class BinaryTableSection
{
    public required string Name { get; init; }
    public required int RecordSize { get; init; }
    public required byte[] Records { get; init; }
    public required uint RecordCount { get; init; }
    public int[] SlotArray { get; init; } = Array.Empty<int>();
}

