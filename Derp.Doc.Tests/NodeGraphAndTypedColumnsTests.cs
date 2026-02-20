using Derp.Doc.Editor;
using Derp.Doc.Model;
using Derp.Doc.Plugins;
using Derp.Doc.Storage;
using Derp.Doc.Tables;

namespace Derp.Doc.Tests;

public sealed class NodeGraphAndTypedColumnsTests
{
    [Fact]
    public void ProjectStorage_RoundTripsFixedVectorValuesWithoutDrift()
    {
        string tempProjectDirectory = Path.Combine(
            Path.GetTempPath(),
            "DerpDocVecRoundTrip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempProjectDirectory);

        try
        {
            var nodeTable = new DocTable { Name = "Nodes", FileName = "nodes" };
            var positionColumn = new DocColumn
            {
                Name = "Pos",
                Kind = DocColumnKind.Vec2,
                ColumnTypeId = DocColumnTypeIds.Vec2Fixed64,
            };
            var tintColumn = new DocColumn
            {
                Name = "Tint",
                Kind = DocColumnKind.Color,
                ColumnTypeId = DocColumnTypeIds.ColorLdr,
            };
            nodeTable.Columns.Add(positionColumn);
            nodeTable.Columns.Add(tintColumn);

            var nodeRow = new DocRow { Id = "node_1" };
            nodeRow.SetCell(positionColumn.Id, DocCellValue.Vec2(1.123456789, -3.987654321));
            nodeRow.SetCell(tintColumn.Id, DocCellValue.Color(1.25, -0.4, 0.5, 3.2));
            nodeTable.Rows.Add(nodeRow);

            var project = new DocProject { Name = "TypedRoundTrip" };
            project.Tables.Add(nodeTable);

            ProjectSerializer.Save(project, tempProjectDirectory);
            DocProject firstLoad = ProjectLoader.Load(tempProjectDirectory);
            ProjectSerializer.Save(firstLoad, tempProjectDirectory);
            DocProject secondLoad = ProjectLoader.Load(tempProjectDirectory);

            DocTable firstLoadedTable = firstLoad.Tables[0];
            DocTable secondLoadedTable = secondLoad.Tables[0];
            DocCellValue firstLoadedPosition = firstLoadedTable.Rows[0].GetCell(firstLoadedTable.Columns[0]);
            DocCellValue secondLoadedPosition = secondLoadedTable.Rows[0].GetCell(secondLoadedTable.Columns[0]);
            DocCellValue secondLoadedTint = secondLoadedTable.Rows[0].GetCell(secondLoadedTable.Columns[1]);

            Assert.Equal(firstLoadedPosition.XValue, secondLoadedPosition.XValue);
            Assert.Equal(firstLoadedPosition.YValue, secondLoadedPosition.YValue);
            Assert.InRange(secondLoadedTint.XValue, 0d, 1d);
            Assert.InRange(secondLoadedTint.YValue, 0d, 1d);
            Assert.InRange(secondLoadedTint.ZValue, 0d, 1d);
            Assert.InRange(secondLoadedTint.WValue, 0d, 1d);
        }
        finally
        {
            Directory.Delete(tempProjectDirectory, recursive: true);
        }
    }

    [Fact]
    public void FormulaEngine_EvaluatesVectorAndColorMath()
    {
        var graphTable = new DocTable { Name = "Graph", FileName = "graph" };
        var inputVectorColumn = new DocColumn { Name = "InputVec", Kind = DocColumnKind.Vec3 };
        var outputVectorColumn = new DocColumn
        {
            Name = "OutputVec",
            Kind = DocColumnKind.Vec3,
            FormulaExpression = "thisRow.InputVec + Vec3(1, 2, 3)",
        };
        var inputColorColumn = new DocColumn
        {
            Name = "InputColor",
            Kind = DocColumnKind.Color,
            ColumnTypeId = DocColumnTypeIds.ColorHdr,
        };
        var outputColorColumn = new DocColumn
        {
            Name = "OutputColor",
            Kind = DocColumnKind.Color,
            ColumnTypeId = DocColumnTypeIds.ColorHdr,
            FormulaExpression = "thisRow.InputColor * 0.5 + Color(0.1, 0.2, 0.3, 0.4)",
        };
        var extractXColumn = new DocColumn
        {
            Name = "ExtractX",
            Kind = DocColumnKind.Number,
            FormulaExpression = "thisRow.OutputVec.x",
        };

        graphTable.Columns.Add(inputVectorColumn);
        graphTable.Columns.Add(outputVectorColumn);
        graphTable.Columns.Add(inputColorColumn);
        graphTable.Columns.Add(outputColorColumn);
        graphTable.Columns.Add(extractXColumn);

        var row = new DocRow { Id = "node_math" };
        row.SetCell(inputVectorColumn.Id, DocCellValue.Vec3(4, 5, 6));
        row.SetCell(inputColorColumn.Id, DocCellValue.Color(0.2, 0.4, 0.6, 0.8));
        graphTable.Rows.Add(row);

        var project = new DocProject { Name = "FormulaVecColor" };
        project.Tables.Add(graphTable);

        var formulaEngine = new DocFormulaEngine();
        formulaEngine.EvaluateProject(project, DocFormulaEngine.EvaluationRequest.Full());

        DocCellValue outputVector = row.GetCell(outputVectorColumn);
        DocCellValue outputColor = row.GetCell(outputColorColumn);
        DocCellValue extractX = row.GetCell(extractXColumn);

        Assert.Equal(5d, outputVector.XValue);
        Assert.Equal(7d, outputVector.YValue);
        Assert.Equal(9d, outputVector.ZValue);
        Assert.Equal(0.2d, outputColor.XValue);
        Assert.Equal(0.4d, outputColor.YValue);
        Assert.Equal(0.6d, outputColor.ZValue);
        Assert.Equal(0.8d, outputColor.WValue);
        Assert.Equal(5d, extractX.NumberValue);
    }

    [Fact]
    public void FormulaEngine_PerCellFormulaOverridesColumnFormula()
    {
        var table = new DocTable { Name = "Metrics", FileName = "metrics" };
        var baseColumn = new DocColumn { Name = "Base", Kind = DocColumnKind.Number };
        var valueColumn = new DocColumn
        {
            Name = "Value",
            Kind = DocColumnKind.Number,
            FormulaExpression = "thisRow.Base + 1",
        };
        table.Columns.Add(baseColumn);
        table.Columns.Add(valueColumn);

        var firstRow = new DocRow { Id = "row_a" };
        firstRow.SetCell(baseColumn.Id, DocCellValue.Number(10));
        var firstOverrideCell = DocCellValue.Number(0);
        firstOverrideCell.CellFormulaExpression = "thisRow.Base + 5";
        firstRow.SetCell(valueColumn.Id, firstOverrideCell);
        table.Rows.Add(firstRow);

        var secondRow = new DocRow { Id = "row_b" };
        secondRow.SetCell(baseColumn.Id, DocCellValue.Number(20));
        table.Rows.Add(secondRow);

        var project = new DocProject { Name = "PerCellOverride" };
        project.Tables.Add(table);

        var formulaEngine = new DocFormulaEngine();
        formulaEngine.EvaluateProject(project, DocFormulaEngine.EvaluationRequest.Full());

        Assert.Equal(15d, firstRow.GetCell(valueColumn).NumberValue);
        Assert.Equal(21d, secondRow.GetCell(valueColumn).NumberValue);
        Assert.Equal("thisRow.Base + 5", firstRow.GetCell(valueColumn).CellFormulaExpression);
    }

    [Fact]
    public void NodeGraphScaffold_CreatesRequiredNodeAndEdgeSchema()
    {
        var workspace = CreateIsolatedWorkspace();
        var nodeTable = new DocTable { Name = "AbilityGraph", FileName = "ability_graph" };
        var payloadColumn = new DocColumn { Name = "Payload", Kind = DocColumnKind.Number };
        nodeTable.Columns.Add(payloadColumn);
        nodeTable.Rows.Add(new DocRow { Id = "node_1" });
        var graphView = new DocView
        {
            Type = DocViewType.Custom,
            CustomRendererId = "builtin.node-graph",
            Name = "Node Graph",
        };
        nodeTable.Views.Add(graphView);

        var project = new DocProject { Name = "NodeGraphScaffoldProject" };
        project.Tables.Add(nodeTable);
        workspace.Project = project;
        workspace.ActiveTable = nodeTable;
        workspace.ActiveTableView = graphView;

        bool scaffoldApplied = NodeGraphTableViewRenderer.TryScaffoldSchemaForTests(
            workspace,
            nodeTable,
            graphView,
            out string statusMessage);

        Assert.True(scaffoldApplied);
        Assert.False(string.IsNullOrWhiteSpace(statusMessage));

        DocColumn typeColumn = FindColumnByName(nodeTable, "Type");
        DocColumn positionColumn = FindColumnByName(nodeTable, "Pos");
        DocColumn titleColumn = FindColumnByName(nodeTable, "Title");
        DocColumn edgesColumn = FindColumnByName(nodeTable, "Edges");

        Assert.Equal(DocColumnKind.Select, typeColumn.Kind);
        Assert.Equal(DocColumnKind.Vec2, positionColumn.Kind);
        Assert.Equal(DocColumnTypeIds.Vec2Fixed64, DocColumnTypeIdMapper.Resolve(positionColumn.ColumnTypeId, positionColumn.Kind));
        Assert.Equal(DocColumnKind.Text, titleColumn.Kind);
        Assert.Equal(DocColumnKind.Subtable, edgesColumn.Kind);
        Assert.False(string.IsNullOrWhiteSpace(edgesColumn.SubtableId));

        DocTable edgeTable = FindTableById(workspace.Project, edgesColumn.SubtableId!);
        Assert.Equal(nodeTable.Id, edgeTable.ParentTableId);
        Assert.False(string.IsNullOrWhiteSpace(edgeTable.ParentRowColumnId));

        DocColumn fromNodeColumn = FindColumnByName(edgeTable, "FromNode");
        DocColumn fromPinColumn = FindColumnByName(edgeTable, "FromPinId");
        DocColumn toNodeColumn = FindColumnByName(edgeTable, "ToNode");
        DocColumn toPinColumn = FindColumnByName(edgeTable, "ToPinId");

        Assert.Equal(DocColumnKind.Relation, fromNodeColumn.Kind);
        Assert.Equal(nodeTable.Id, fromNodeColumn.RelationTableId);
        Assert.Equal(DocColumnKind.Text, fromPinColumn.Kind);
        Assert.Equal(DocColumnKind.Relation, toNodeColumn.Kind);
        Assert.Equal(nodeTable.Id, toNodeColumn.RelationTableId);
        Assert.Equal(DocColumnKind.Text, toPinColumn.Kind);
    }

    [Fact]
    public void FormulaEngine_GraphInput_ResolvesEdges_AndRecomputesWhenEdgeTableChanges()
    {
        var nodeTable = new DocTable { Name = "AbilityGraph", FileName = "ability_graph" };
        var typeColumn = new DocColumn
        {
            Name = "Type",
            Kind = DocColumnKind.Select,
            Options = new List<string> { "Default" },
        };
        var positionColumn = new DocColumn
        {
            Name = "Pos",
            Kind = DocColumnKind.Vec2,
            ColumnTypeId = DocColumnTypeIds.Vec2Fixed64,
        };
        var outputColumn = new DocColumn { Name = "OutValue", Kind = DocColumnKind.Number };
        var inputColumn = new DocColumn { Name = "InValue", Kind = DocColumnKind.Number };
        var edgesColumn = new DocColumn
        {
            Name = "Edges",
            Kind = DocColumnKind.Subtable,
        };
        nodeTable.Columns.Add(typeColumn);
        nodeTable.Columns.Add(positionColumn);
        nodeTable.Columns.Add(outputColumn);
        nodeTable.Columns.Add(inputColumn);
        nodeTable.Columns.Add(edgesColumn);

        var sourceRowA = new DocRow { Id = "node_source_a" };
        sourceRowA.SetCell(typeColumn.Id, DocCellValue.Text("Default"));
        sourceRowA.SetCell(positionColumn.Id, DocCellValue.Vec2(10, 20));
        sourceRowA.SetCell(outputColumn.Id, DocCellValue.Number(2));
        nodeTable.Rows.Add(sourceRowA);

        var sourceRowB = new DocRow { Id = "node_source_b" };
        sourceRowB.SetCell(typeColumn.Id, DocCellValue.Text("Default"));
        sourceRowB.SetCell(positionColumn.Id, DocCellValue.Vec2(40, 20));
        sourceRowB.SetCell(outputColumn.Id, DocCellValue.Number(7));
        nodeTable.Rows.Add(sourceRowB);

        var targetRow = new DocRow { Id = "node_target" };
        targetRow.SetCell(typeColumn.Id, DocCellValue.Text("Default"));
        targetRow.SetCell(positionColumn.Id, DocCellValue.Vec2(80, 20));
        var inputCellWithGraphFormula = DocCellValue.Number(0);
        inputCellWithGraphFormula.CellFormulaExpression = "graph.in(\"" + inputColumn.Id + "\")";
        targetRow.SetCell(inputColumn.Id, inputCellWithGraphFormula);
        nodeTable.Rows.Add(targetRow);

        var edgeTable = new DocTable
        {
            Name = "AbilityGraph Edges",
            FileName = "ability_graph_edges",
            ParentTableId = nodeTable.Id,
        };
        var parentRowColumn = new DocColumn
        {
            Name = "_parentRowId",
            Kind = DocColumnKind.Text,
            IsHidden = true,
        };
        edgeTable.ParentRowColumnId = parentRowColumn.Id;
        var fromNodeColumn = new DocColumn
        {
            Name = "FromNode",
            Kind = DocColumnKind.Relation,
            RelationTableId = nodeTable.Id,
            RelationDisplayColumnId = typeColumn.Id,
        };
        var fromPinColumn = new DocColumn { Name = "FromPinId", Kind = DocColumnKind.Text };
        var toNodeColumn = new DocColumn
        {
            Name = "ToNode",
            Kind = DocColumnKind.Relation,
            RelationTableId = nodeTable.Id,
            RelationDisplayColumnId = typeColumn.Id,
        };
        var toPinColumn = new DocColumn { Name = "ToPinId", Kind = DocColumnKind.Text };
        edgeTable.Columns.Add(parentRowColumn);
        edgeTable.Columns.Add(fromNodeColumn);
        edgeTable.Columns.Add(fromPinColumn);
        edgeTable.Columns.Add(toNodeColumn);
        edgeTable.Columns.Add(toPinColumn);

        var edgeRow = new DocRow { Id = "edge_1" };
        edgeRow.SetCell(parentRowColumn.Id, DocCellValue.Text(targetRow.Id));
        edgeRow.SetCell(fromNodeColumn.Id, DocCellValue.Text(sourceRowA.Id));
        edgeRow.SetCell(fromPinColumn.Id, DocCellValue.Text(outputColumn.Id));
        edgeRow.SetCell(toNodeColumn.Id, DocCellValue.Text(targetRow.Id));
        edgeRow.SetCell(toPinColumn.Id, DocCellValue.Text(inputColumn.Id));
        edgeTable.Rows.Add(edgeRow);

        edgesColumn.SubtableId = edgeTable.Id;

        var project = new DocProject { Name = "GraphInFormulaProject" };
        project.Tables.Add(nodeTable);
        project.Tables.Add(edgeTable);

        var formulaEngine = new DocFormulaEngine();
        formulaEngine.EvaluateProject(project, DocFormulaEngine.EvaluationRequest.Full());
        Assert.Equal(2d, targetRow.GetCell(inputColumn).NumberValue);

        edgeRow.SetCell(fromNodeColumn.Id, DocCellValue.Text(sourceRowB.Id));
        formulaEngine.EvaluateProject(
            project,
            DocFormulaEngine.EvaluationRequest.Incremental(
                new List<string> { edgeTable.Id },
                refreshDirtyTableIndexes: false));

        Assert.Equal(7d, targetRow.GetCell(inputColumn).NumberValue);
    }

    [Fact]
    public void FormulaEngine_RelationColumns_RespectSelfAndParentTargetModes()
    {
        var parentTable = new DocTable { Name = "DialogNodes", FileName = "dialog_nodes" };
        var parentTitleColumn = new DocColumn { Name = "Title", Kind = DocColumnKind.Text };
        parentTable.Columns.Add(parentTitleColumn);
        var parentRow = new DocRow { Id = "dialog_root" };
        parentRow.SetCell(parentTitleColumn.Id, DocCellValue.Text("Root"));
        parentTable.Rows.Add(parentRow);

        var selfRelationColumn = new DocColumn
        {
            Name = "SelfLink",
            Kind = DocColumnKind.Relation,
            RelationTargetMode = DocRelationTargetMode.SelfTable,
            FormulaExpression = "thisRow",
        };
        var selfTable = new DocTable { Name = "SelfNodes", FileName = "self_nodes" };
        selfTable.Columns.Add(selfRelationColumn);
        var selfRow = new DocRow { Id = "self_1" };
        selfTable.Rows.Add(selfRow);

        var childTable = new DocTable
        {
            Name = "Choices",
            FileName = "choices",
            ParentTableId = parentTable.Id,
        };
        var parentRowColumn = new DocColumn
        {
            Name = "_parentRowId",
            Kind = DocColumnKind.Text,
            IsHidden = true,
        };
        childTable.ParentRowColumnId = parentRowColumn.Id;
        var parentRelationColumn = new DocColumn
        {
            Name = "NextNode",
            Kind = DocColumnKind.Relation,
            RelationTargetMode = DocRelationTargetMode.ParentTable,
            FormulaExpression = "parentRow",
        };
        childTable.Columns.Add(parentRowColumn);
        childTable.Columns.Add(parentRelationColumn);
        var childRow = new DocRow { Id = "choice_a" };
        childRow.SetCell(parentRowColumn.Id, DocCellValue.Text(parentRow.Id));
        childTable.Rows.Add(childRow);

        var project = new DocProject { Name = "RelationTargetModes" };
        project.Tables.Add(parentTable);
        project.Tables.Add(selfTable);
        project.Tables.Add(childTable);

        var formulaEngine = new DocFormulaEngine();
        formulaEngine.EvaluateProject(project, DocFormulaEngine.EvaluationRequest.Full());

        Assert.Equal(selfRow.Id, selfRow.GetCell(selfRelationColumn).StringValue);
        Assert.Equal(parentRow.Id, childRow.GetCell(parentRelationColumn).StringValue);
    }

    [Fact]
    public void ProjectStorage_Load_ResolvesParentRelationTargetMode()
    {
        string tempProjectDirectory = Path.Combine(
            Path.GetTempPath(),
            "DerpDocRelationTargetMode_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempProjectDirectory);

        try
        {
            var parentTable = new DocTable { Name = "Parent", FileName = "parent" };
            parentTable.Columns.Add(new DocColumn { Name = "Label", Kind = DocColumnKind.Text });
            parentTable.Rows.Add(new DocRow { Id = "parent_1" });

            var childTable = new DocTable
            {
                Name = "Child",
                FileName = "child",
                ParentTableId = parentTable.Id,
            };
            var parentRowColumn = new DocColumn
            {
                Name = "_parentRowId",
                Kind = DocColumnKind.Text,
                IsHidden = true,
            };
            childTable.ParentRowColumnId = parentRowColumn.Id;
            var parentRelationColumn = new DocColumn
            {
                Name = "ParentRef",
                Kind = DocColumnKind.Relation,
                RelationTargetMode = DocRelationTargetMode.ParentTable,
            };
            childTable.Columns.Add(parentRowColumn);
            childTable.Columns.Add(parentRelationColumn);
            childTable.Rows.Add(new DocRow { Id = "child_1" });

            var project = new DocProject { Name = "RelationModeRoundTrip" };
            project.Tables.Add(parentTable);
            project.Tables.Add(childTable);

            ProjectSerializer.Save(project, tempProjectDirectory);
            DocProject loadedProject = ProjectLoader.Load(tempProjectDirectory);

            DocTable loadedChildTable = loadedProject.Tables.Single(table => string.Equals(table.Name, "Child", StringComparison.Ordinal));
            DocColumn loadedRelationColumn = loadedChildTable.Columns.Single(column => string.Equals(column.Name, "ParentRef", StringComparison.Ordinal));
            Assert.Equal(DocRelationTargetMode.ParentTable, loadedRelationColumn.RelationTargetMode);
            Assert.Equal(parentTable.Id, loadedRelationColumn.RelationTableId);
        }
        finally
        {
            Directory.Delete(tempProjectDirectory, recursive: true);
        }
    }

    private static DocColumn FindColumnByName(DocTable table, string name)
    {
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn column = table.Columns[columnIndex];
            if (string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return column;
            }
        }

        throw new InvalidOperationException("Column not found: " + name);
    }

    private static DocTable FindTableById(DocProject project, string tableId)
    {
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable table = project.Tables[tableIndex];
            if (string.Equals(table.Id, tableId, StringComparison.Ordinal))
            {
                return table;
            }
        }

        throw new InvalidOperationException("Table not found: " + tableId);
    }

    private static DocWorkspace CreateIsolatedWorkspace()
    {
        var workspace = new DocWorkspace();
        workspace.AutoSave = false;
        workspace.ProjectPath = null;
        return workspace;
    }
}
