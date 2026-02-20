namespace Derp.Doc.Model;

public enum DocViewType
{
    Grid,
    Board,
    Calendar,
    Chart,
    Custom,
}

public enum DocChartKind
{
    Bar,
    Line,
    Pie,
    Area,
}

public enum DocViewFilterOp
{
    Equals,
    NotEquals,
    Contains,
    NotContains,
    GreaterThan,
    LessThan,
    IsEmpty,
    IsNotEmpty,
}

public sealed class DocViewSort
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ColumnId { get; set; } = "";
    public bool Descending { get; set; }
    public DocViewBinding? ColumnIdBinding { get; set; }
    public DocViewBinding? DescendingBinding { get; set; }

    public DocViewSort Clone() => new()
    {
        Id = Id,
        ColumnId = ColumnId,
        Descending = Descending,
        ColumnIdBinding = ColumnIdBinding?.Clone(),
        DescendingBinding = DescendingBinding?.Clone(),
    };
}

public sealed class DocViewFilter
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ColumnId { get; set; } = "";
    public DocViewFilterOp Op { get; set; } = DocViewFilterOp.Equals;
    public string Value { get; set; } = "";
    public DocViewBinding? ColumnIdBinding { get; set; }
    public DocViewBinding? OpBinding { get; set; }
    public DocViewBinding? ValueBinding { get; set; }

    public DocViewFilter Clone() => new()
    {
        Id = Id,
        ColumnId = ColumnId,
        Op = Op,
        Value = Value,
        ColumnIdBinding = ColumnIdBinding?.Clone(),
        OpBinding = OpBinding?.Clone(),
        ValueBinding = ValueBinding?.Clone(),
    };
}

public sealed class DocView
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Grid view";
    public DocViewType Type { get; set; } = DocViewType.Grid;

    /// <summary>
    /// Ordered list of column IDs visible in this view. Null = show all non-hidden columns.
    /// </summary>
    public List<string>? VisibleColumnIds { get; set; }

    public List<DocViewSort> Sorts { get; set; } = new();
    public List<DocViewFilter> Filters { get; set; } = new();

    /// <summary>
    /// Board view: the Select column used to group rows into lanes.
    /// </summary>
    public string? GroupByColumnId { get; set; }
    public DocViewBinding? GroupByColumnBinding { get; set; }

    /// <summary>
    /// Calendar view: the text column containing date strings (yyyy-MM-dd or MM/dd/yyyy).
    /// </summary>
    public string? CalendarDateColumnId { get; set; }
    public DocViewBinding? CalendarDateColumnBinding { get; set; }

    /// <summary>
    /// Chart view: the chart sub-type (Bar/Line/Pie).
    /// </summary>
    public DocChartKind? ChartKind { get; set; }
    public DocViewBinding? ChartKindBinding { get; set; }

    /// <summary>
    /// Chart view: the column used for X-axis categories or pie labels.
    /// </summary>
    public string? ChartCategoryColumnId { get; set; }
    public DocViewBinding? ChartCategoryColumnBinding { get; set; }

    /// <summary>
    /// Chart view: the Number column used for Y-axis values or pie slice sizes.
    /// </summary>
    public string? ChartValueColumnId { get; set; }
    public DocViewBinding? ChartValueColumnBinding { get; set; }

    /// <summary>
    /// Custom view: plugin renderer ID from TableViewRendererRegistry.
    /// </summary>
    public string? CustomRendererId { get; set; }

    public DocView Clone()
    {
        var clone = new DocView
        {
            Id = Id,
            Name = Name,
            Type = Type,
            VisibleColumnIds = VisibleColumnIds != null ? new List<string>(VisibleColumnIds) : null,
            GroupByColumnId = GroupByColumnId,
            GroupByColumnBinding = GroupByColumnBinding?.Clone(),
            CalendarDateColumnId = CalendarDateColumnId,
            CalendarDateColumnBinding = CalendarDateColumnBinding?.Clone(),
            ChartKind = ChartKind,
            ChartKindBinding = ChartKindBinding?.Clone(),
            ChartCategoryColumnId = ChartCategoryColumnId,
            ChartCategoryColumnBinding = ChartCategoryColumnBinding?.Clone(),
            ChartValueColumnId = ChartValueColumnId,
            ChartValueColumnBinding = ChartValueColumnBinding?.Clone(),
            CustomRendererId = CustomRendererId,
        };

        for (int i = 0; i < Sorts.Count; i++)
            clone.Sorts.Add(Sorts[i].Clone());

        for (int i = 0; i < Filters.Count; i++)
            clone.Filters.Add(Filters[i].Clone());

        return clone;
    }
}
