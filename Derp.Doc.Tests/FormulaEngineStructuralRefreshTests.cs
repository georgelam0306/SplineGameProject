using System.Linq;
using Derp.Doc.Model;
using Derp.Doc.Tables;

namespace Derp.Doc.Tests;

public sealed class FormulaEngineStructuralRefreshTests
{
    [Fact]
    public void FullEvaluation_RebuildsTableLookups_AfterAddingJoinSourceTable()
    {
        var items = new DocTable { Name = "Items", FileName = "items" };
        var itemKey = new DocColumn { Name = "Key", Kind = DocColumnKind.Text };
        items.Columns.Add(itemKey);
        items.Rows.Add(MakeRow("item_a", itemKey, "A"));

        var derived = new DocTable { Name = "InventoryView", FileName = "inventoryview" };
        const string outKeyColumnId = "out_key";
        derived.Columns.Add(new DocColumn
        {
            Id = outKeyColumnId,
            Name = "Key",
            Kind = DocColumnKind.Text,
            IsProjected = true,
        });
        derived.DerivedConfig = new DocDerivedConfig
        {
            BaseTableId = items.Id,
            Projections =
            {
                new DerivedProjection
                {
                    SourceTableId = items.Id,
                    SourceColumnId = itemKey.Id,
                    OutputColumnId = outKeyColumnId,
                }
            }
        };

        var project = new DocProject { Name = "StructuralRefreshRegression" };
        project.Tables.Add(items);
        project.Tables.Add(derived);

        var engine = new DocFormulaEngine();
        engine.EvaluateProject(project, DocFormulaEngine.EvaluationRequest.Full());
        Assert.Single(derived.Rows);

        var stats = new DocTable { Name = "Stats", FileName = "stats" };
        var statKey = new DocColumn { Name = "Key", Kind = DocColumnKind.Text };
        var statValue = new DocColumn { Name = "Value", Kind = DocColumnKind.Number };
        stats.Columns.Add(statKey);
        stats.Columns.Add(statValue);
        stats.Rows.Add(MakeRow("stat_a", statKey, "A", statValue, 42));
        project.Tables.Add(stats);

        const string outValueColumnId = "out_value";
        derived.Columns.Add(new DocColumn
        {
            Id = outValueColumnId,
            Name = "Value",
            Kind = DocColumnKind.Number,
            IsProjected = true,
        });

        var config = derived.DerivedConfig!;
        config.Steps.Add(new DerivedStep
        {
            Kind = DerivedStepKind.Join,
            SourceTableId = stats.Id,
            JoinKind = DerivedJoinKind.Left,
            KeyMappings = { new DerivedKeyMapping { BaseColumnId = outKeyColumnId, SourceColumnId = statKey.Id } },
        });
        config.Projections.Add(new DerivedProjection
        {
            SourceTableId = stats.Id,
            SourceColumnId = statValue.Id,
            OutputColumnId = outValueColumnId,
        });

        engine.EvaluateProject(project, DocFormulaEngine.EvaluationRequest.Full());

        Assert.Contains(
            config.Projections,
            projection => projection.SourceTableId == stats.Id &&
                          projection.SourceColumnId == statValue.Id &&
                          projection.OutputColumnId == outValueColumnId);

        var outValueColumn = derived.Columns.Single(column => column.Id == outValueColumnId);
        Assert.Equal(42, derived.Rows.Single().GetCell(outValueColumn).NumberValue);
    }

    private static DocRow MakeRow(string rowId, DocColumn keyColumn, string keyValue)
    {
        var row = new DocRow { Id = rowId };
        row.SetCell(keyColumn.Id, DocCellValue.Text(keyValue));
        return row;
    }

    private static DocRow MakeRow(
        string rowId,
        DocColumn keyColumn,
        string keyValue,
        DocColumn valueColumn,
        double value)
    {
        var row = new DocRow { Id = rowId };
        row.SetCell(keyColumn.Id, DocCellValue.Text(keyValue));
        row.SetCell(valueColumn.Id, DocCellValue.Number(value));
        return row;
    }
}
