namespace Derp.Doc.Export;

internal sealed class ExportViewBindingModel
{
    public required string ViewId { get; init; }
    public required string OutputName { get; init; }
    public required ExportViewBindingTargetKind TargetKind { get; init; }
    public required string TargetItemId { get; init; }
    public required string Expression { get; init; }
}
