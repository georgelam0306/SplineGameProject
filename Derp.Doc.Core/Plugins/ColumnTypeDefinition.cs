using Derp.Doc.Model;

namespace Derp.Doc.Plugins;

public sealed class ColumnTypeDefinition
{
    public required string ColumnTypeId { get; init; }

    public required string DisplayName { get; init; }

    public string? IconGlyph { get; init; }

    public DocColumnKind FallbackKind { get; init; } = DocColumnKind.Text;

    public bool IsTextWrappedByDefault { get; init; }

    public float? MinimumRowHeight { get; init; }
}
