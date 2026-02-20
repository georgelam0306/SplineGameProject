namespace Derp.Doc.Model;

public static class DocRelationTargetResolver
{
    public static string? ResolveTargetTableId(DocTable sourceTable, DocColumn relationColumn)
    {
        ArgumentNullException.ThrowIfNull(sourceTable);
        ArgumentNullException.ThrowIfNull(relationColumn);
        return ResolveTargetTableId(sourceTable, relationColumn.RelationTargetMode, relationColumn.RelationTableId);
    }

    public static string? ResolveTargetTableId(
        DocTable sourceTable,
        DocRelationTargetMode relationTargetMode,
        string? externalTableId)
    {
        ArgumentNullException.ThrowIfNull(sourceTable);
        return relationTargetMode switch
        {
            DocRelationTargetMode.SelfTable => sourceTable.Id,
            DocRelationTargetMode.ParentTable => string.IsNullOrWhiteSpace(sourceTable.ParentTableId)
                ? null
                : sourceTable.ParentTableId,
            _ => string.IsNullOrWhiteSpace(externalTableId)
                ? null
                : externalTableId,
        };
    }
}
