using Derp.Doc.Model;

namespace Derp.Doc.Export;

internal sealed class ExportTableVariableModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string SlotName { get; init; }
    public required DocColumnKind Kind { get; init; }
    public required string ColumnTypeId { get; init; }
    public required string Expression { get; init; }
    public bool HasExpression => !string.IsNullOrWhiteSpace(Expression);
}
