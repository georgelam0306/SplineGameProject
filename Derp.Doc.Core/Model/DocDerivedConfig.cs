namespace Derp.Doc.Model;

/// <summary>
/// Configuration for a derived (computed) table — an Append/Join pipeline
/// that materializes rows from other tables.
/// </summary>
public sealed class DocDerivedConfig
{
    /// <summary>
    /// Base table ID for Join pipelines. Null for pure Append.
    /// </summary>
    public string? BaseTableId { get; set; }

    /// <summary>
    /// Ordered pipeline steps (Append or Join).
    /// </summary>
    public List<DerivedStep> Steps { get; set; } = new();

    /// <summary>
    /// Which source columns appear in the output and in what order.
    /// </summary>
    public List<DerivedProjection> Projections { get; set; } = new();

    /// <summary>
    /// Source columns that should not be projected into the output, even if present in sources.
    /// This preserves user intent when we auto-project new/missing source columns by default.
    /// </summary>
    public List<DerivedProjectionSuppression> SuppressedProjections { get; set; } = new();

    /// <summary>
    /// Optional row-level filter expression applied after the append/join pipeline.
    /// </summary>
    public string FilterExpression { get; set; } = "";

    public DocDerivedConfig Clone()
    {
        var clone = new DocDerivedConfig
        {
            BaseTableId = BaseTableId,
            FilterExpression = FilterExpression,
        };
        for (int i = 0; i < Steps.Count; i++)
            clone.Steps.Add(Steps[i].Clone());
        for (int i = 0; i < Projections.Count; i++)
            clone.Projections.Add(Projections[i].Clone());
        for (int i = 0; i < SuppressedProjections.Count; i++)
            clone.SuppressedProjections.Add(SuppressedProjections[i].Clone());
        return clone;
    }
}

public sealed class DerivedStep
{
    /// <summary>
    /// Stable identity for this step instance.
    /// Used for Append row identity so appending the same source table twice does not collide.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public DerivedStepKind Kind { get; set; }
    public string SourceTableId { get; set; } = "";
    public DerivedJoinKind JoinKind { get; set; } = DerivedJoinKind.Left;
    public List<DerivedKeyMapping> KeyMappings { get; set; } = new();

    public DerivedStep Clone()
    {
        var clone = new DerivedStep
        {
            Id = Id,
            Kind = Kind,
            SourceTableId = SourceTableId,
            JoinKind = JoinKind,
        };
        for (int i = 0; i < KeyMappings.Count; i++)
            clone.KeyMappings.Add(KeyMappings[i].Clone());
        return clone;
    }
}

public enum DerivedStepKind
{
    Append,
    Join,
}

public enum DerivedJoinKind
{
    Left,
    Inner,
    FullOuter,
}

public sealed class DerivedKeyMapping
{
    public string BaseColumnId { get; set; } = "";
    public string SourceColumnId { get; set; } = "";

    public DerivedKeyMapping Clone() => new()
    {
        BaseColumnId = BaseColumnId,
        SourceColumnId = SourceColumnId,
    };
}

public sealed class DerivedProjection
{
    public string SourceTableId { get; set; } = "";
    public string SourceColumnId { get; set; } = "";

    /// <summary>
    /// The column ID in the derived table's Columns list that this projection maps to.
    /// </summary>
    public string OutputColumnId { get; set; } = "";

    /// <summary>
    /// Optional display alias. If empty, uses the source column name.
    /// </summary>
    public string RenameAlias { get; set; } = "";

    public DerivedProjection Clone() => new()
    {
        SourceTableId = SourceTableId,
        SourceColumnId = SourceColumnId,
        OutputColumnId = OutputColumnId,
        RenameAlias = RenameAlias,
    };
}

public sealed class DerivedProjectionSuppression
{
    public string SourceTableId { get; set; } = "";
    public string SourceColumnId { get; set; } = "";
    public string OutputColumnId { get; set; } = "";

    public DerivedProjectionSuppression Clone() => new()
    {
        SourceTableId = SourceTableId,
        SourceColumnId = SourceColumnId,
        OutputColumnId = OutputColumnId,
    };
}

/// <summary>
/// Stable identity key for a row in a derived table.
/// For Append: (appendStepId, sourceRowId).
/// For Join: (baseTableId, baseRowId).
/// </summary>
public readonly struct OutRowKey : IEquatable<OutRowKey>
{
    public readonly string TableId;
    public readonly string? RowId;

    public OutRowKey(string tableId, string? rowId)
    {
        TableId = tableId;
        RowId = rowId;
    }

    public bool Equals(OutRowKey other) =>
        string.Equals(TableId, other.TableId, StringComparison.Ordinal) &&
        string.Equals(RowId, other.RowId, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is OutRowKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        TableId?.GetHashCode(StringComparison.Ordinal) ?? 0,
        RowId?.GetHashCode(StringComparison.Ordinal) ?? 0);

    public static bool operator ==(OutRowKey left, OutRowKey right) => left.Equals(right);
    public static bool operator !=(OutRowKey left, OutRowKey right) => !left.Equals(right);
}

public enum DerivedMatchState
{
    Matched,
    NoMatch,
    MultiMatch,
    TypeMismatch,
}
