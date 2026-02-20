using Derp.Doc.Commands;
using Derp.Doc.Editor;
using Derp.Doc.Model;
using Derp.Doc.Storage;
using Derp.Doc.Tables;

namespace Derp.Doc.Tests;

public sealed class Phase3SmokeTests
{
    [Fact]
    public void FormulaEngine_ComputesIntraTableDependencies()
    {
        var valueTable = new DocTable { Name = "Metrics", FileName = "metrics" };
        var valueColumn = new DocColumn { Name = "Value", Kind = DocColumnKind.Number };
        var plusOneColumn = new DocColumn
        {
            Name = "PlusOne",
            Kind = DocColumnKind.Number,
            FormulaExpression = "thisRow.Value + 1"
        };
        var doubledColumn = new DocColumn
        {
            Name = "Doubled",
            Kind = DocColumnKind.Number,
            FormulaExpression = "thisRow.PlusOne * 2"
        };
        valueTable.Columns.Add(valueColumn);
        valueTable.Columns.Add(plusOneColumn);
        valueTable.Columns.Add(doubledColumn);

        var metricRow = new DocRow();
        metricRow.SetCell(valueColumn.Id, DocCellValue.Number(5));
        valueTable.Rows.Add(metricRow);

        var project = new DocProject { Name = "FormulaSmokeProject" };
        project.Tables.Add(valueTable);

        var formulaEngine = new DocFormulaEngine();
        formulaEngine.EvaluateProject(project);

        Assert.Equal(6, metricRow.GetCell(plusOneColumn).NumberValue);
        Assert.Equal(12, metricRow.GetCell(doubledColumn).NumberValue);

        metricRow.SetCell(valueColumn.Id, DocCellValue.Number(7));
        formulaEngine.EvaluateProject(project);

        Assert.Equal(8, metricRow.GetCell(plusOneColumn).NumberValue);
        Assert.Equal(16, metricRow.GetCell(doubledColumn).NumberValue);
    }

    [Fact]
    public void FormulaEngine_ComputesCrossTableFunctionsAndMethodChains()
    {
        var tasksTable = new DocTable { Name = "Tasks", FileName = "tasks" };
        var taskNameColumn = new DocColumn { Name = "Name", Kind = DocColumnKind.Text };
        var assigneeColumn = new DocColumn { Name = "Assignee", Kind = DocColumnKind.Text };
        var hoursColumn = new DocColumn { Name = "Hours", Kind = DocColumnKind.Number };
        var doneColumn = new DocColumn { Name = "Done", Kind = DocColumnKind.Checkbox };
        tasksTable.Columns.Add(taskNameColumn);
        tasksTable.Columns.Add(assigneeColumn);
        tasksTable.Columns.Add(hoursColumn);
        tasksTable.Columns.Add(doneColumn);

        tasksTable.Rows.Add(CreateTaskRow(taskNameColumn, assigneeColumn, hoursColumn, doneColumn, "Design", "Alice", 3, false));
        tasksTable.Rows.Add(CreateTaskRow(taskNameColumn, assigneeColumn, hoursColumn, doneColumn, "Review", "Alice", 5, true));
        tasksTable.Rows.Add(CreateTaskRow(taskNameColumn, assigneeColumn, hoursColumn, doneColumn, "Implement", "Bob", 2, false));

        var peopleTable = new DocTable { Name = "People", FileName = "people" };
        var peopleNameColumn = new DocColumn { Name = "Name", Kind = DocColumnKind.Text };
        var openCountColumn = new DocColumn
        {
            Name = "OpenCount",
            Kind = DocColumnKind.Number,
            FormulaExpression = "Tasks.Filter(@Assignee == thisRow.Name && !@Done).Count()"
        };
        var doneHoursColumn = new DocColumn
        {
            Name = "DoneHours",
            Kind = DocColumnKind.Number,
            FormulaExpression = "SumIf(Tasks, @Assignee == thisRow.Name && @Done, @Hours)"
        };
        var firstOpenColumn = new DocColumn
        {
            Name = "FirstOpen",
            Kind = DocColumnKind.Text,
            FormulaExpression = "Tasks.Filter(@Assignee == thisRow.Name && !@Done).Sort(@Hours).First().Name"
        };
        var openAverageColumn = new DocColumn
        {
            Name = "OpenAverage",
            Kind = DocColumnKind.Number,
            FormulaExpression = "Tasks.Filter(@Assignee == thisRow.Name && !@Done).Average(@Hours)"
        };
        peopleTable.Columns.Add(peopleNameColumn);
        peopleTable.Columns.Add(openCountColumn);
        peopleTable.Columns.Add(doneHoursColumn);
        peopleTable.Columns.Add(firstOpenColumn);
        peopleTable.Columns.Add(openAverageColumn);

        peopleTable.Rows.Add(CreatePersonRow(peopleNameColumn, "Alice"));
        peopleTable.Rows.Add(CreatePersonRow(peopleNameColumn, "Bob"));
        peopleTable.Rows.Add(CreatePersonRow(peopleNameColumn, "Charlie"));

        var project = new DocProject { Name = "CrossTableProject" };
        project.Tables.Add(tasksTable);
        project.Tables.Add(peopleTable);

        var formulaEngine = new DocFormulaEngine();
        formulaEngine.EvaluateProject(project);

        var aliceRow = peopleTable.Rows[0];
        Assert.Equal(1, aliceRow.GetCell(openCountColumn).NumberValue);
        Assert.Equal(5, aliceRow.GetCell(doneHoursColumn).NumberValue);
        Assert.Equal("Design", aliceRow.GetCell(firstOpenColumn).StringValue);
        Assert.Equal(3, aliceRow.GetCell(openAverageColumn).NumberValue);

        var bobRow = peopleTable.Rows[1];
        Assert.Equal(1, bobRow.GetCell(openCountColumn).NumberValue);
        Assert.Equal(0, bobRow.GetCell(doneHoursColumn).NumberValue);
        Assert.Equal("Implement", bobRow.GetCell(firstOpenColumn).StringValue);
        Assert.Equal(2, bobRow.GetCell(openAverageColumn).NumberValue);

        var charlieRow = peopleTable.Rows[2];
        Assert.Equal(0, charlieRow.GetCell(openCountColumn).NumberValue);
        Assert.Equal(0, charlieRow.GetCell(doneHoursColumn).NumberValue);
        Assert.Equal("", charlieRow.GetCell(firstOpenColumn).StringValue);
        Assert.Equal(0, charlieRow.GetCell(openAverageColumn).NumberValue);
    }

    [Fact]
    public void FormulaEngine_EvaluatesExpPowAndAbsFunctions()
    {
        var table = new DocTable { Name = "Math", FileName = "math" };
        var inputColumn = new DocColumn { Name = "Input", Kind = DocColumnKind.Number };
        var powBaseColumn = new DocColumn { Name = "PowBase", Kind = DocColumnKind.Number };
        var powExponentColumn = new DocColumn { Name = "PowExponent", Kind = DocColumnKind.Number };
        var absInputColumn = new DocColumn { Name = "AbsInput", Kind = DocColumnKind.Number };
        var expColumn = new DocColumn
        {
            Name = "ExpValue",
            Kind = DocColumnKind.Number,
            FormulaExpression = "Exp(thisRow.Input)"
        };
        var powColumn = new DocColumn
        {
            Name = "PowValue",
            Kind = DocColumnKind.Number,
            FormulaExpression = "Pow(thisRow.PowBase, thisRow.PowExponent)"
        };
        var absColumn = new DocColumn
        {
            Name = "AbsValue",
            Kind = DocColumnKind.Number,
            FormulaExpression = "Abs(thisRow.AbsInput)"
        };
        table.Columns.Add(inputColumn);
        table.Columns.Add(powBaseColumn);
        table.Columns.Add(powExponentColumn);
        table.Columns.Add(absInputColumn);
        table.Columns.Add(expColumn);
        table.Columns.Add(powColumn);
        table.Columns.Add(absColumn);

        var row = new DocRow();
        row.SetCell(inputColumn.Id, DocCellValue.Number(1));
        row.SetCell(powBaseColumn.Id, DocCellValue.Number(2));
        row.SetCell(powExponentColumn.Id, DocCellValue.Number(3));
        row.SetCell(absInputColumn.Id, DocCellValue.Number(-4));
        table.Rows.Add(row);

        var project = new DocProject { Name = "MathProject" };
        project.Tables.Add(table);

        var formulaEngine = new DocFormulaEngine();
        formulaEngine.EvaluateProject(project);

        Assert.Equal(Math.E, row.GetCell(expColumn).NumberValue, 5);
        Assert.Equal(8, row.GetCell(powColumn).NumberValue);
        Assert.Equal(4, row.GetCell(absColumn).NumberValue);
    }

    [Fact]
    public void FormulaEngine_EvaluatesEvalSplineFunction()
    {
        var table = new DocTable { Name = "Curves", FileName = "curves" };
        var curveColumn = new DocColumn { Name = "Curve", Kind = DocColumnKind.Spline };
        var tColumn = new DocColumn { Name = "T", Kind = DocColumnKind.Number };
        var sampleColumn = new DocColumn
        {
            Name = "Sample",
            Kind = DocColumnKind.Number,
            FormulaExpression = "EvalSpline(thisRow.Curve, thisRow.T)"
        };
        table.Columns.Add(curveColumn);
        table.Columns.Add(tColumn);
        table.Columns.Add(sampleColumn);

        SplineUtils.SplinePoint[] curvedPoints =
        [
            new SplineUtils.SplinePoint(0f, 0f, 0f, 2f),
            new SplineUtils.SplinePoint(1f, 1f, 0f, 0f),
        ];
        string curvedJson = SplineUtils.Serialize(curvedPoints);

        var row = new DocRow();
        row.SetCell(curveColumn.Id, DocCellValue.Text(curvedJson));
        row.SetCell(tColumn.Id, DocCellValue.Number(0.5));
        table.Rows.Add(row);

        var project = new DocProject { Name = "SplineProject" };
        project.Tables.Add(table);

        var formulaEngine = new DocFormulaEngine();
        formulaEngine.EvaluateProject(project);
        Assert.Equal(0.75, row.GetCell(sampleColumn).NumberValue, 6);

        row.SetCell(curveColumn.Id, DocCellValue.Text(SplineUtils.DefaultSplineJson));
        formulaEngine.EvaluateProject(project);
        Assert.Equal(0.5, row.GetCell(sampleColumn).NumberValue, 6);
    }

    [Fact]
    public void DocCellValue_Default_ForSpline_UsesDefaultSplineJson()
    {
        var defaultValue = DocCellValue.Default(DocColumnKind.Spline);
        Assert.Equal(SplineUtils.DefaultSplineJson, defaultValue.StringValue);
    }

    [Fact]
    public void DocCellValue_Default_ForAssetKinds_UsesEmptyString()
    {
        var defaultTexture = DocCellValue.Default(DocColumnKind.TextureAsset);
        var defaultMesh = DocCellValue.Default(DocColumnKind.MeshAsset);
        var defaultAudio = DocCellValue.Default(DocColumnKind.AudioAsset);

        Assert.Equal("", defaultTexture.StringValue);
        Assert.Equal("", defaultMesh.StringValue);
        Assert.Equal("", defaultAudio.StringValue);
    }

    [Fact]
    public void FormulaEngine_ResolvesDocumentVariables_InTableFormulas_WithForwardReferences()
    {
        var table = new DocTable { Name = "Metrics", FileName = "metrics" };
        var valueColumn = new DocColumn { Name = "Value", Kind = DocColumnKind.Number };
        var taxColumn = new DocColumn
        {
            Name = "Tax",
            Kind = DocColumnKind.Number,
            FormulaExpression = "docs.finance.gross_rate"
        };
        table.Columns.Add(valueColumn);
        table.Columns.Add(taxColumn);

        var row = new DocRow();
        row.SetCell(valueColumn.Id, DocCellValue.Number(100));
        table.Rows.Add(row);

        var document = new DocDocument
        {
            Title = "Finance",
            FileName = "finance",
            Blocks = new List<DocBlock>
            {
                new()
                {
                    Type = DocBlockType.Variable,
                    Text = new RichText { PlainText = "@gross_rate = thisDoc.tax_rate * 2" }
                },
                new()
                {
                    Type = DocBlockType.Variable,
                    Text = new RichText { PlainText = "@tax_rate = 0.2" }
                }
            }
        };

        var project = new DocProject
        {
            Name = "DocScopeProject",
            Tables = new List<DocTable> { table },
            Documents = new List<DocDocument> { document }
        };

        var formulaEngine = new DocFormulaEngine();
        formulaEngine.EvaluateProject(project);

        Assert.Equal(0.4, row.GetCell(taxColumn).NumberValue, 4);
    }

    [Fact]
    public void FormulaEngine_ReturnsFormulaError_ForInvalidDocumentVariableExpression()
    {
        var table = new DocTable { Name = "Metrics", FileName = "metrics" };
        var valueColumn = new DocColumn { Name = "Value", Kind = DocColumnKind.Number };
        var taxColumn = new DocColumn
        {
            Name = "Tax",
            Kind = DocColumnKind.Number,
            FormulaExpression = "docs.finance.tax_rate"
        };
        table.Columns.Add(valueColumn);
        table.Columns.Add(taxColumn);

        var row = new DocRow();
        row.SetCell(valueColumn.Id, DocCellValue.Number(100));
        table.Rows.Add(row);

        var document = new DocDocument
        {
            Title = "Finance",
            FileName = "finance",
            Blocks = new List<DocBlock>
            {
                new()
                {
                    Type = DocBlockType.Variable,
                    Text = new RichText { PlainText = "@tax_rate = 1 +" }
                }
            }
        };

        var project = new DocProject
        {
            Name = "DocScopeProject",
            Tables = new List<DocTable> { table },
            Documents = new List<DocDocument> { document }
        };

        var formulaEngine = new DocFormulaEngine();
        formulaEngine.EvaluateProject(project);

        Assert.Equal("#ERR", row.GetCell(taxColumn).StringValue);
    }

    [Fact]
    public void FormulaEngine_ResolvesDocumentVariableAtReferences_AndNormalizedEqualsExpressions()
    {
        var table = new DocTable { Name = "Metrics", FileName = "metrics" };
        var valueColumn = new DocColumn { Name = "Value", Kind = DocColumnKind.Number };
        var taxColumn = new DocColumn
        {
            Name = "Tax",
            Kind = DocColumnKind.Number,
            FormulaExpression = "docs.finance.gross_rate"
        };
        table.Columns.Add(valueColumn);
        table.Columns.Add(taxColumn);

        var row = new DocRow();
        row.SetCell(valueColumn.Id, DocCellValue.Number(100));
        table.Rows.Add(row);

        var document = new DocDocument
        {
            Title = "Finance",
            FileName = "finance",
            Blocks = new List<DocBlock>
            {
                new()
                {
                    Type = DocBlockType.Variable,
                    Text = new RichText { PlainText = "@gross_rate = @tax_rate * 2" }
                },
                new()
                {
                    Type = DocBlockType.Variable,
                    Text = new RichText { PlainText = "@tax_rate = =0.2" }
                }
            }
        };

        var project = new DocProject
        {
            Name = "DocScopeProject",
            Tables = new List<DocTable> { table },
            Documents = new List<DocDocument> { document }
        };

        var formulaEngine = new DocFormulaEngine();
        formulaEngine.EvaluateProject(project);

        Assert.Equal(0.4, row.GetCell(taxColumn).NumberValue, 4);
    }

    [Fact]
    public void FormulaEngine_EvaluatesAtDocumentVariable_InDocumentExpressions()
    {
        var document = new DocDocument
        {
            Title = "Finance",
            FileName = "finance",
            Blocks = new List<DocBlock>
            {
                new()
                {
                    Type = DocBlockType.Variable,
                    Text = new RichText { PlainText = "@tax_rate = 0.2" }
                }
            }
        };

        var project = new DocProject
        {
            Name = "DocScopeProject",
            Documents = new List<DocDocument> { document }
        };

        var formulaEngine = new DocFormulaEngine();
        bool evaluated = formulaEngine.TryEvaluateDocumentExpression(project, document, "@tax_rate * 2", out string resultText);

        Assert.True(evaluated);
        Assert.Equal("0.4", resultText);
    }

    [Fact]
    public void FormulaEngine_ResolvesRelationTraversal()
    {
        var peopleTable = new DocTable { Name = "People", FileName = "people" };
        var peopleNameColumn = new DocColumn { Name = "Name", Kind = DocColumnKind.Text };
        peopleTable.Columns.Add(peopleNameColumn);

        var aliceRow = CreatePersonRow(peopleNameColumn, "Alice");
        var bobRow = CreatePersonRow(peopleNameColumn, "Bob");
        peopleTable.Rows.Add(aliceRow);
        peopleTable.Rows.Add(bobRow);

        var tasksTable = new DocTable { Name = "Tasks", FileName = "tasks" };
        var taskTitleColumn = new DocColumn { Name = "Title", Kind = DocColumnKind.Text };
        var ownerColumn = new DocColumn
        {
            Name = "Owner",
            Kind = DocColumnKind.Relation,
            RelationTableId = peopleTable.Id
        };
        var ownerNameColumn = new DocColumn
        {
            Name = "OwnerName",
            Kind = DocColumnKind.Text,
            FormulaExpression = "thisRow.Owner.Name"
        };
        tasksTable.Columns.Add(taskTitleColumn);
        tasksTable.Columns.Add(ownerColumn);
        tasksTable.Columns.Add(ownerNameColumn);

        var taskRowOne = new DocRow();
        taskRowOne.SetCell(taskTitleColumn.Id, DocCellValue.Text("Fix bug"));
        taskRowOne.SetCell(ownerColumn.Id, DocCellValue.Text(aliceRow.Id));
        tasksTable.Rows.Add(taskRowOne);

        var taskRowTwo = new DocRow();
        taskRowTwo.SetCell(taskTitleColumn.Id, DocCellValue.Text("Ship build"));
        taskRowTwo.SetCell(ownerColumn.Id, DocCellValue.Text(bobRow.Id));
        tasksTable.Rows.Add(taskRowTwo);

        var project = new DocProject { Name = "RelationProject" };
        project.Tables.Add(peopleTable);
        project.Tables.Add(tasksTable);

        var formulaEngine = new DocFormulaEngine();
        formulaEngine.EvaluateProject(project);

        Assert.Equal("Alice", taskRowOne.GetCell(ownerNameColumn).StringValue);
        Assert.Equal("Bob", taskRowTwo.GetCell(ownerNameColumn).StringValue);
    }

    [Fact]
    public void FormulaEngine_SetsError_WhenFormulaOutputDoesNotMatchColumnType()
    {
        var table = new DocTable { Name = "Mismatch", FileName = "mismatch" };
        var numberColumn = new DocColumn
        {
            Name = "AsNumber",
            Kind = DocColumnKind.Number,
            FormulaExpression = "\"oops\""
        };
        table.Columns.Add(numberColumn);
        table.Rows.Add(new DocRow());

        var project = new DocProject { Name = "MismatchProject" };
        project.Tables.Add(table);

        var formulaEngine = new DocFormulaEngine();
        formulaEngine.EvaluateProject(project);

        Assert.Equal("#ERR", table.Rows[0].GetCell(numberColumn).StringValue);
    }

    [Fact]
    public void FormulaEngine_ExposesOneBasedThisRowIndex()
    {
        var table = new DocTable { Name = "Rows", FileName = "rows" };
        var nameColumn = new DocColumn { Name = "Name", Kind = DocColumnKind.Text };
        var rowIndexColumn = new DocColumn
        {
            Name = "RowIndex",
            Kind = DocColumnKind.Number,
            FormulaExpression = "thisRowIndex"
        };
        table.Columns.Add(nameColumn);
        table.Columns.Add(rowIndexColumn);

        table.Rows.Add(CreatePersonRow(nameColumn, "First"));
        table.Rows.Add(CreatePersonRow(nameColumn, "Second"));
        table.Rows.Add(CreatePersonRow(nameColumn, "Third"));

        var project = new DocProject { Name = "IndexProject" };
        project.Tables.Add(table);

        var formulaEngine = new DocFormulaEngine();
        formulaEngine.EvaluateProject(project);

        Assert.Equal(1, table.Rows[0].GetCell(rowIndexColumn).NumberValue);
        Assert.Equal(2, table.Rows[1].GetCell(rowIndexColumn).NumberValue);
        Assert.Equal(3, table.Rows[2].GetCell(rowIndexColumn).NumberValue);
    }

    [Fact]
    public void FormulaEngine_ExposesOneBasedCandidateRowIndex()
    {
        var sourceTable = new DocTable { Name = "Source", FileName = "source" };
        var sourceKeyColumn = new DocColumn { Name = "Key", Kind = DocColumnKind.Text };
        sourceTable.Columns.Add(sourceKeyColumn);
        sourceTable.Rows.Add(CreatePersonRow(sourceKeyColumn, "A"));
        sourceTable.Rows.Add(CreatePersonRow(sourceKeyColumn, "B"));
        sourceTable.Rows.Add(CreatePersonRow(sourceKeyColumn, "C"));

        var targetTable = new DocTable { Name = "Target", FileName = "target" };
        var targetKeyColumn = new DocColumn { Name = "Key", Kind = DocColumnKind.Text };
        var lookupIndexColumn = new DocColumn
        {
            Name = "LookupIndex",
            Kind = DocColumnKind.Number,
            FormulaExpression = "Lookup(Source, @Key == thisRow.Key, @rowIndex)"
        };
        var chainIndexColumn = new DocColumn
        {
            Name = "ChainIndex",
            Kind = DocColumnKind.Number,
            FormulaExpression = "Source.Filter(@Key == thisRow.Key).First().rowIndex"
        };
        targetTable.Columns.Add(targetKeyColumn);
        targetTable.Columns.Add(lookupIndexColumn);
        targetTable.Columns.Add(chainIndexColumn);

        targetTable.Rows.Add(CreatePersonRow(targetKeyColumn, "B"));
        targetTable.Rows.Add(CreatePersonRow(targetKeyColumn, "C"));

        var project = new DocProject { Name = "CandidateIndexProject" };
        project.Tables.Add(sourceTable);
        project.Tables.Add(targetTable);

        var formulaEngine = new DocFormulaEngine();
        formulaEngine.EvaluateProject(project);

        Assert.Equal(2, targetTable.Rows[0].GetCell(lookupIndexColumn).NumberValue);
        Assert.Equal(3, targetTable.Rows[1].GetCell(lookupIndexColumn).NumberValue);
        Assert.Equal(2, targetTable.Rows[0].GetCell(chainIndexColumn).NumberValue);
        Assert.Equal(3, targetTable.Rows[1].GetCell(chainIndexColumn).NumberValue);
    }

    [Fact]
    public void FormulaEngine_ExposesParentRowAndParentTable_ForSubtableRows()
    {
        var project = BuildParentScopeFormulaProject(
            out var parentTable,
            out var parentRow,
            out var baseWeightColumn,
            out var childTable,
            out _,
            out var weightColumn,
            out var parentCountColumn,
            out var childRow);

        var formulaEngine = new DocFormulaEngine();
        formulaEngine.EvaluateProject(project);

        Assert.Equal(parentRow.GetCell(baseWeightColumn).NumberValue + 3, childRow.GetCell(weightColumn).NumberValue);
        Assert.Equal(1, childRow.GetCell(parentCountColumn).NumberValue);
    }

    [Fact]
    public void FormulaEngine_Incremental_ReevaluatesParentScopeFormulas_WhenParentTableChanges()
    {
        var project = BuildParentScopeFormulaProject(
            out var parentTable,
            out var parentRow,
            out var baseWeightColumn,
            out _,
            out _,
            out var weightColumn,
            out _,
            out var childRow);

        var formulaEngine = new DocFormulaEngine();
        formulaEngine.EvaluateProject(project, DocFormulaEngine.EvaluationRequest.Full());
        Assert.Equal(5, childRow.GetCell(weightColumn).NumberValue);

        parentRow.SetCell(baseWeightColumn.Id, DocCellValue.Number(10));
        var metrics = formulaEngine.EvaluateProject(
            project,
            DocFormulaEngine.EvaluationRequest.Incremental(new[] { parentTable.Id }));

        Assert.True(metrics.UsedIncrementalPlan);
        Assert.Equal(13, childRow.GetCell(weightColumn).NumberValue);
    }

    [Fact]
    public void FormulaEngine_IncrementalTargeted_ReevaluatesOnlyTargetColumnsAndDependencies()
    {
        var table = new DocTable { Name = "PreviewTargets", FileName = "preview_targets" };
        var inputColumn = new DocColumn { Name = "Input", Kind = DocColumnKind.Number };
        var plusOneColumn = new DocColumn
        {
            Name = "PlusOne",
            Kind = DocColumnKind.Number,
            FormulaExpression = "thisRow.Input + 1"
        };
        var plusTwoColumn = new DocColumn
        {
            Name = "PlusTwo",
            Kind = DocColumnKind.Number,
            FormulaExpression = "thisRow.PlusOne + 1"
        };
        var expensiveColumn = new DocColumn
        {
            Name = "Expensive",
            Kind = DocColumnKind.Number,
            FormulaExpression = "thisRow.Input * 10"
        };

        table.Columns.Add(inputColumn);
        table.Columns.Add(plusOneColumn);
        table.Columns.Add(plusTwoColumn);
        table.Columns.Add(expensiveColumn);

        var row = new DocRow();
        row.SetCell(inputColumn.Id, DocCellValue.Number(2));
        table.Rows.Add(row);

        var project = new DocProject { Name = "PreviewTargetProject" };
        project.Tables.Add(table);

        var formulaEngine = new DocFormulaEngine();
        formulaEngine.EvaluateProject(project, DocFormulaEngine.EvaluationRequest.Full());
        Assert.Equal(4, row.GetCell(plusTwoColumn).NumberValue);
        Assert.Equal(20, row.GetCell(expensiveColumn).NumberValue);

        row.SetCell(inputColumn.Id, DocCellValue.Number(5));
        var targetedColumnsByTable = new Dictionary<string, List<string>>(StringComparer.Ordinal)
        {
            [table.Id] = new List<string> { plusTwoColumn.Id }
        };

        formulaEngine.EvaluateProject(
            project,
            DocFormulaEngine.EvaluationRequest.IncrementalTargeted(
                new[] { table.Id },
                targetedColumnsByTable,
                refreshDirtyTableIndexes: false));

        Assert.Equal(6, row.GetCell(plusOneColumn).NumberValue);
        Assert.Equal(7, row.GetCell(plusTwoColumn).NumberValue);
        Assert.Equal(20, row.GetCell(expensiveColumn).NumberValue);

        formulaEngine.EvaluateProject(
            project,
            DocFormulaEngine.EvaluationRequest.Incremental(
                new[] { table.Id },
                refreshDirtyTableIndexes: false));

        Assert.Equal(50, row.GetCell(expensiveColumn).NumberValue);
    }

    [Fact]
    public void Workspace_RecalculatesFormulas_OnExecuteUndoRedo()
    {
        var scoreTable = new DocTable { Name = "Scores", FileName = "scores" };
        var pointsColumn = new DocColumn { Name = "Points", Kind = DocColumnKind.Number };
        var scoreColumn = new DocColumn
        {
            Name = "Score",
            Kind = DocColumnKind.Number,
            FormulaExpression = "thisRow.Points * 3"
        };
        scoreTable.Columns.Add(pointsColumn);
        scoreTable.Columns.Add(scoreColumn);

        var scoreRow = new DocRow();
        scoreRow.SetCell(pointsColumn.Id, DocCellValue.Number(2));
        scoreTable.Rows.Add(scoreRow);

        var workspace = CreateIsolatedWorkspace();
        workspace.Project = new DocProject { Name = "WorkspaceProject", Tables = new List<DocTable> { scoreTable } };
        workspace.ActiveTable = scoreTable;

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetCell,
            TableId = scoreTable.Id,
            RowId = scoreRow.Id,
            ColumnId = pointsColumn.Id,
            OldCellValue = DocCellValue.Number(2),
            NewCellValue = DocCellValue.Number(4)
        });

        Assert.Equal(12, scoreRow.GetCell(scoreColumn).NumberValue);

        workspace.Undo();
        Assert.Equal(6, scoreRow.GetCell(scoreColumn).NumberValue);

        workspace.Redo();
        Assert.Equal(12, scoreRow.GetCell(scoreColumn).NumberValue);
    }

    [Fact]
    public void CommandImpact_Metadata_ClassifiesFormulaRelevantCommands()
    {
        Assert.True(DocCommandImpact.RequiresFormulaRecalculation(DocCommandKind.SetCell));
        Assert.True(DocCommandImpact.RequiresFormulaRecalculation(DocCommandKind.SetDerivedConfig));
        Assert.True(DocCommandImpact.RequiresFormulaRecalculation(DocCommandKind.AddBlock));
        Assert.True(DocCommandImpact.RequiresFormulaRecalculation(DocCommandKind.SetBlockText));
        Assert.False(DocCommandImpact.RequiresFormulaRecalculation(DocCommandKind.UpdateViewConfig));
    }

    [Fact]
    public void Workspace_DocumentCommands_DoNotTriggerFormulaRecalculation()
    {
        var scoreTable = new DocTable { Name = "Scores", FileName = "scores_doc_ops" };
        var pointsColumn = new DocColumn { Name = "Points", Kind = DocColumnKind.Number };
        var scoreColumn = new DocColumn
        {
            Name = "Score",
            Kind = DocColumnKind.Number,
            FormulaExpression = "thisRow.Points * 3"
        };
        scoreTable.Columns.Add(pointsColumn);
        scoreTable.Columns.Add(scoreColumn);

        var scoreRow = new DocRow();
        scoreRow.SetCell(pointsColumn.Id, DocCellValue.Number(2));
        scoreTable.Rows.Add(scoreRow);

        var document = new DocDocument { Title = "Notes", FileName = "notes" };
        document.Blocks.Add(new DocBlock
        {
            Type = DocBlockType.Paragraph,
            Order = "a0",
            Text = new RichText { PlainText = "One" }
        });

        var workspace = CreateIsolatedWorkspace();
        workspace.Project = new DocProject
        {
            Name = "MixedProject",
            Tables = new List<DocTable> { scoreTable },
            Documents = new List<DocDocument> { document }
        };
        workspace.ActiveTable = scoreTable;
        workspace.ActiveDocument = document;

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetCell,
            TableId = scoreTable.Id,
            RowId = scoreRow.Id,
            ColumnId = pointsColumn.Id,
            OldCellValue = DocCellValue.Number(2),
            NewCellValue = DocCellValue.Number(3)
        });

        Assert.Equal(9, scoreRow.GetCell(scoreColumn).NumberValue);
        var baselinePerf = workspace.GetPerformanceCounters();

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.AddBlock,
            DocumentId = document.Id,
            BlockIndex = 1,
            BlockSnapshot = new DocBlock
            {
                Type = DocBlockType.Paragraph,
                Order = "a1",
                Text = new RichText { PlainText = "Two" }
            }
        });

        var afterAddPerf = workspace.GetPerformanceCounters();
        Assert.Equal(baselinePerf.FormulaRecalculationCount, afterAddPerf.FormulaRecalculationCount);
        Assert.Equal(9, scoreRow.GetCell(scoreColumn).NumberValue);

        workspace.Undo();
        var afterUndoPerf = workspace.GetPerformanceCounters();
        Assert.Equal(afterAddPerf.FormulaRecalculationCount, afterUndoPerf.FormulaRecalculationCount);
        Assert.Equal(9, scoreRow.GetCell(scoreColumn).NumberValue);

        workspace.Redo();
        var afterRedoPerf = workspace.GetPerformanceCounters();
        Assert.Equal(afterUndoPerf.FormulaRecalculationCount, afterRedoPerf.FormulaRecalculationCount);
        Assert.Equal(9, scoreRow.GetCell(scoreColumn).NumberValue);
    }

    [Fact]
    public void Workspace_ColumnWidthCommand_IsUndoable()
    {
        var table = new DocTable { Name = "Sizing", FileName = "sizing" };
        var textColumn = new DocColumn { Name = "Name", Kind = DocColumnKind.Text, Width = 140f };
        table.Columns.Add(textColumn);

        var workspace = CreateIsolatedWorkspace();
        workspace.Project = new DocProject { Name = "SizingProject", Tables = new List<DocTable> { table } };
        workspace.ActiveTable = table;

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetColumnWidth,
            TableId = table.Id,
            ColumnId = textColumn.Id,
            OldColumnWidth = 140f,
            NewColumnWidth = 260f
        });

        Assert.Equal(260f, textColumn.Width);

        workspace.Undo();
        Assert.Equal(140f, textColumn.Width);

        workspace.Redo();
        Assert.Equal(260f, textColumn.Width);
    }

    [Fact]
    public void Workspace_MoveColumnCommand_IsUndoable()
    {
        var table = new DocTable { Name = "Ordering", FileName = "ordering" };
        var firstColumn = new DocColumn { Name = "First", Kind = DocColumnKind.Text };
        var secondColumn = new DocColumn { Name = "Second", Kind = DocColumnKind.Number };
        var thirdColumn = new DocColumn { Name = "Third", Kind = DocColumnKind.Checkbox };
        table.Columns.Add(firstColumn);
        table.Columns.Add(secondColumn);
        table.Columns.Add(thirdColumn);

        var workspace = CreateIsolatedWorkspace();
        workspace.Project = new DocProject { Name = "OrderingProject", Tables = new List<DocTable> { table } };
        workspace.ActiveTable = table;

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.MoveColumn,
            TableId = table.Id,
            ColumnId = firstColumn.Id,
            ColumnIndex = 0,
            TargetColumnIndex = 3
        });

        Assert.Equal("Second", table.Columns[0].Name);
        Assert.Equal("Third", table.Columns[1].Name);
        Assert.Equal("First", table.Columns[2].Name);

        workspace.Undo();
        Assert.Equal("First", table.Columns[0].Name);
        Assert.Equal("Second", table.Columns[1].Name);
        Assert.Equal("Third", table.Columns[2].Name);

        workspace.Redo();
        Assert.Equal("Second", table.Columns[0].Name);
        Assert.Equal("Third", table.Columns[1].Name);
        Assert.Equal("First", table.Columns[2].Name);
    }

    [Fact]
    public void Workspace_MoveRowCommand_IsUndoable()
    {
        var table = new DocTable { Name = "RowOrdering", FileName = "row_ordering" };
        var nameColumn = new DocColumn { Name = "Name", Kind = DocColumnKind.Text };
        table.Columns.Add(nameColumn);
        table.Rows.Add(CreatePersonRow(nameColumn, "First"));
        table.Rows.Add(CreatePersonRow(nameColumn, "Second"));
        table.Rows.Add(CreatePersonRow(nameColumn, "Third"));

        var workspace = CreateIsolatedWorkspace();
        workspace.Project = new DocProject { Name = "RowOrderingProject", Tables = new List<DocTable> { table } };
        workspace.ActiveTable = table;

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.MoveRow,
            TableId = table.Id,
            RowIndex = 0,
            TargetRowIndex = 3
        });

        Assert.Equal("Second", table.Rows[0].GetCell(nameColumn).StringValue);
        Assert.Equal("Third", table.Rows[1].GetCell(nameColumn).StringValue);
        Assert.Equal("First", table.Rows[2].GetCell(nameColumn).StringValue);

        workspace.Undo();
        Assert.Equal("First", table.Rows[0].GetCell(nameColumn).StringValue);
        Assert.Equal("Second", table.Rows[1].GetCell(nameColumn).StringValue);
        Assert.Equal("Third", table.Rows[2].GetCell(nameColumn).StringValue);

        workspace.Redo();
        Assert.Equal("Second", table.Rows[0].GetCell(nameColumn).StringValue);
        Assert.Equal("Third", table.Rows[1].GetCell(nameColumn).StringValue);
        Assert.Equal("First", table.Rows[2].GetCell(nameColumn).StringValue);
    }

    [Fact]
    public void ProjectStorage_RoundTripsFormulaAndRelationSchema()
    {
        string tempProjectDirectory = Path.Combine(
            Path.GetTempPath(),
            "DerpDocPhase3Smoke_" + Guid.NewGuid().ToString("N"));

        try
        {
            var peopleTable = new DocTable { Name = "People", FileName = "people" };
            var peopleNameColumn = new DocColumn { Name = "Name", Kind = DocColumnKind.Text };
            peopleTable.Columns.Add(peopleNameColumn);
            peopleTable.Rows.Add(CreatePersonRow(peopleNameColumn, "Alice"));

            var tasksTable = new DocTable { Name = "Tasks", FileName = "tasks" };
            var ownerColumn = new DocColumn
            {
                Name = "Owner",
                Kind = DocColumnKind.Relation,
                RelationTableId = peopleTable.Id,
                Width = 210f
            };
            var effortColumn = new DocColumn { Name = "Effort", Kind = DocColumnKind.Number };
            var normalizedEffortColumn = new DocColumn
            {
                Name = "NormalizedEffort",
                Kind = DocColumnKind.Number,
                FormulaExpression = "thisRow.Effort / 2",
                Width = 185f
            };
            tasksTable.Columns.Add(ownerColumn);
            tasksTable.Columns.Add(effortColumn);
            tasksTable.Columns.Add(normalizedEffortColumn);
            var taskRow = new DocRow();
            taskRow.SetCell(ownerColumn.Id, DocCellValue.Text(peopleTable.Rows[0].Id));
            taskRow.SetCell(effortColumn.Id, DocCellValue.Number(10));
            tasksTable.Rows.Add(taskRow);

            var project = new DocProject
            {
                Name = "StorageSmokeProject",
                Tables = new List<DocTable> { peopleTable, tasksTable }
            };

            ProjectSerializer.Save(project, tempProjectDirectory);
            var loadedProject = ProjectLoader.Load(tempProjectDirectory);

            var loadedTasksTable = loadedProject.Tables.Find(table => table.Name == "Tasks");
            Assert.NotNull(loadedTasksTable);

            var loadedRelationColumn = loadedTasksTable!.Columns.Find(column => column.Name == "Owner");
            Assert.NotNull(loadedRelationColumn);
            Assert.Equal(DocColumnKind.Relation, loadedRelationColumn!.Kind);
            Assert.Equal(peopleTable.Id, loadedRelationColumn.RelationTableId);
            Assert.Equal(210f, loadedRelationColumn.Width);

            var loadedFormulaColumn = loadedTasksTable.Columns.Find(column => column.Name == "NormalizedEffort");
            Assert.NotNull(loadedFormulaColumn);
            Assert.Equal(DocColumnKind.Number, loadedFormulaColumn!.Kind);
            Assert.Equal("thisRow.Effort / 2", loadedFormulaColumn.FormulaExpression);
            Assert.Equal(185f, loadedFormulaColumn.Width);
        }
        finally
        {
            if (Directory.Exists(tempProjectDirectory))
            {
                Directory.Delete(tempProjectDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ProjectStorage_RoundTripsFormulaEvalScopes()
    {
        string tempProjectDirectory = Path.Combine(
            Path.GetTempPath(),
            "DerpDocFormulaScopes_" + Guid.NewGuid().ToString("N"));

        try
        {
            var table = new DocTable { Name = "Metrics", FileName = "metrics" };
            var baseColumn = new DocColumn { Name = "Base", Kind = DocColumnKind.Number };
            var previewColumn = new DocColumn
            {
                Name = "Preview",
                Kind = DocColumnKind.Number,
                FormulaExpression = "thisRow.Base + 1",
                FormulaEvalScopes = DocFormulaEvalScope.Interactive | DocFormulaEvalScope.Export,
            };
            table.Columns.Add(baseColumn);
            table.Columns.Add(previewColumn);

            var project = new DocProject
            {
                Name = "FormulaScopeStorage",
                Tables = new List<DocTable> { table },
            };

            ProjectSerializer.Save(project, tempProjectDirectory);
            var loadedProject = ProjectLoader.Load(tempProjectDirectory);
            var loadedTable = loadedProject.Tables[0];
            var loadedColumn = loadedTable.Columns.Find(column => string.Equals(column.Name, "Preview", StringComparison.Ordinal));
            Assert.NotNull(loadedColumn);
            Assert.Equal(
                DocFormulaEvalScope.Interactive | DocFormulaEvalScope.Export,
                loadedColumn!.FormulaEvalScopes);
        }
        finally
        {
            if (Directory.Exists(tempProjectDirectory))
            {
                Directory.Delete(tempProjectDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ProjectStorage_RoundTripsSchemaLinkedTables_AndVariantDeltas()
    {
        string tempProjectDirectory = Path.Combine(
            Path.GetTempPath(),
            "DerpDocVariantStorage_" + Guid.NewGuid().ToString("N"));

        try
        {
            var templateTable = new DocTable
            {
                Name = "UnitTemplate",
                FileName = "unittemplate",
            };
            var idColumn = new DocColumn { Name = "Id", Kind = DocColumnKind.Number };
            var nameColumn = new DocColumn { Name = "Name", Kind = DocColumnKind.Text };
            templateTable.Columns.Add(idColumn);
            templateTable.Columns.Add(nameColumn);

            var linkedTable = new DocTable
            {
                Name = "Units",
                FileName = "units",
                SchemaSourceTableId = templateTable.Id,
            };
            linkedTable.Columns.Add(new DocColumn { Id = idColumn.Id, Name = "Id", Kind = DocColumnKind.Number });
            linkedTable.Columns.Add(new DocColumn { Id = nameColumn.Id, Name = "Name", Kind = DocColumnKind.Text });

            var row = new DocRow { Id = "row0" };
            row.SetCell(idColumn.Id, DocCellValue.Number(0));
            row.SetCell(nameColumn.Id, DocCellValue.Text("Marine"));
            linkedTable.Rows.Add(row);

            var delta = new DocTableVariantDelta { VariantId = 1 };
            delta.CellOverrides.Add(new DocTableCellOverride
            {
                RowId = "row0",
                ColumnId = nameColumn.Id,
                Value = DocCellValue.Text("Marine Elite"),
            });
            linkedTable.VariantDeltas.Add(delta);

            var project = new DocProject
            {
                Name = "StorageVariants",
                Tables = new List<DocTable> { templateTable, linkedTable },
            };
            templateTable.Variants.Add(new DocTableVariant { Id = 1, Name = "Elite" });
            linkedTable.Variants.Add(new DocTableVariant { Id = 1, Name = "Elite" });

            ProjectSerializer.Save(project, tempProjectDirectory);
            var loadedProject = ProjectLoader.Load(tempProjectDirectory);

            DocTable? loadedLinkedTable = loadedProject.Tables.Find(table =>
                string.Equals(table.Name, "Units", StringComparison.Ordinal));
            Assert.NotNull(loadedLinkedTable);
            Assert.Equal(templateTable.Id, loadedLinkedTable!.SchemaSourceTableId);
            Assert.Contains(loadedLinkedTable.Variants, variant => variant.Id == 1 && variant.Name == "Elite");
            Assert.Single(loadedLinkedTable.VariantDeltas);
            Assert.Equal(1, loadedLinkedTable.VariantDeltas[0].VariantId);
            Assert.Single(loadedLinkedTable.VariantDeltas[0].CellOverrides);
            Assert.Equal("row0", loadedLinkedTable.VariantDeltas[0].CellOverrides[0].RowId);
            Assert.Equal(nameColumn.Id, loadedLinkedTable.VariantDeltas[0].CellOverrides[0].ColumnId);
            Assert.Equal("Marine Elite", loadedLinkedTable.VariantDeltas[0].CellOverrides[0].Value.StringValue);
        }
        finally
        {
            if (Directory.Exists(tempProjectDirectory))
            {
                Directory.Delete(tempProjectDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ProjectStorage_SchemaLinkedTables_RemapExistingData_WhenColumnIdsDiffer()
    {
        string tempProjectDirectory = Path.Combine(
            Path.GetTempPath(),
            "DerpDocSchemaLinkRemap_" + Guid.NewGuid().ToString("N"));

        try
        {
            var sourceTable = new DocTable
            {
                Name = "EntityBase",
                FileName = "entity_base",
            };
            sourceTable.Columns.Add(new DocColumn { Id = "id", Name = "Id", Kind = DocColumnKind.Id });
            sourceTable.Columns.Add(new DocColumn { Id = "name", Name = "Name", Kind = DocColumnKind.Text });
            sourceTable.Columns.Add(new DocColumn { Id = "ui_asset", Name = "UiAsset", Kind = DocColumnKind.UiAsset });

            var linkedTable = new DocTable
            {
                Name = "Enemies",
                FileName = "enemies",
                SchemaSourceTableId = sourceTable.Id,
            };
            linkedTable.Columns.Add(new DocColumn { Id = "legacy_id", Name = "Id", Kind = DocColumnKind.Id });
            linkedTable.Columns.Add(new DocColumn { Id = "legacy_name", Name = "Name", Kind = DocColumnKind.Text });
            linkedTable.Columns.Add(new DocColumn { Id = "legacy_ui", Name = "UiAsset", Kind = DocColumnKind.UiAsset });

            var enemyRow = new DocRow { Id = "enemy_1" };
            enemyRow.SetCell("legacy_id", DocCellValue.Text("enemy_1"));
            enemyRow.SetCell("legacy_name", DocCellValue.Text("Spinner"));
            enemyRow.SetCell("legacy_ui", DocCellValue.Text("spinner.bdui"));
            linkedTable.Rows.Add(enemyRow);

            var project = new DocProject
            {
                Name = "SchemaLinkRemap",
                Tables = new List<DocTable> { sourceTable, linkedTable },
            };

            ProjectSerializer.Save(project, tempProjectDirectory);
            DocProject loadedProject = ProjectLoader.Load(tempProjectDirectory);

            DocTable? loadedLinkedTable = loadedProject.Tables.Find(table =>
                string.Equals(table.Id, linkedTable.Id, StringComparison.Ordinal));
            Assert.NotNull(loadedLinkedTable);

            DocColumn? idColumn = loadedLinkedTable!.Columns.Find(column => string.Equals(column.Id, "id", StringComparison.Ordinal));
            DocColumn? nameColumn = loadedLinkedTable.Columns.Find(column => string.Equals(column.Id, "name", StringComparison.Ordinal));
            DocColumn? uiAssetColumn = loadedLinkedTable.Columns.Find(column => string.Equals(column.Id, "ui_asset", StringComparison.Ordinal));
            Assert.NotNull(idColumn);
            Assert.NotNull(nameColumn);
            Assert.NotNull(uiAssetColumn);

            DocRow loadedRow = Assert.Single(loadedLinkedTable.Rows);
            Assert.Equal("enemy_1", loadedRow.GetCell(idColumn!).StringValue);
            Assert.Equal("Spinner", loadedRow.GetCell(nameColumn!).StringValue);
            Assert.Equal("spinner.bdui", loadedRow.GetCell(uiAssetColumn!).StringValue);
        }
        finally
        {
            if (Directory.Exists(tempProjectDirectory))
            {
                Directory.Delete(tempProjectDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ProjectStorage_RoundTripsAssetColumnKinds_PathValues_AndMeshCellMetadata()
    {
        string tempProjectDirectory = Path.Combine(
            Path.GetTempPath(),
            "DerpDocAssetStorage_" + Guid.NewGuid().ToString("N"));

        try
        {
            var table = new DocTable { Name = "Visuals", FileName = "visuals" };
            var textureColumn = new DocColumn { Name = "Texture", Kind = DocColumnKind.TextureAsset };
            var meshColumn = new DocColumn { Name = "Mesh", Kind = DocColumnKind.MeshAsset };
            var audioColumn = new DocColumn { Name = "Voice", Kind = DocColumnKind.AudioAsset };
            table.Columns.Add(textureColumn);
            table.Columns.Add(meshColumn);
            table.Columns.Add(audioColumn);

            var row = new DocRow { Id = "visual-row" };
            row.SetCell(textureColumn.Id, DocCellValue.Text("Textures/hero.png"));
            var meshCell = DocCellValue.Text("Meshes/tree.glb");
            meshCell.ModelPreviewSettings = new DocModelPreviewSettings
            {
                OrbitYawDegrees = -22.5f,
                OrbitPitchDegrees = 14.25f,
                PanX = 0.12f,
                PanY = -0.08f,
                Zoom = 1.35f,
                TextureRelativePath = "Textures/bark.png",
            };
            row.SetCell(meshColumn.Id, meshCell);
            row.SetCell(audioColumn.Id, DocCellValue.Text("Audio/voice_hero.mp3"));
            table.Rows.Add(row);

            var project = new DocProject
            {
                Name = "AssetStorageProject",
                Tables = new List<DocTable> { table },
            };

            ProjectSerializer.Save(project, tempProjectDirectory);
            var loadedProject = ProjectLoader.Load(tempProjectDirectory);
            var loadedTable = loadedProject.Tables[0];

            var loadedTextureColumn = loadedTable.Columns.Find(column => string.Equals(column.Name, "Texture", StringComparison.Ordinal));
            var loadedMeshColumn = loadedTable.Columns.Find(column => string.Equals(column.Name, "Mesh", StringComparison.Ordinal));
            var loadedAudioColumn = loadedTable.Columns.Find(column => string.Equals(column.Name, "Voice", StringComparison.Ordinal));
            Assert.NotNull(loadedTextureColumn);
            Assert.NotNull(loadedMeshColumn);
            Assert.NotNull(loadedAudioColumn);
            Assert.Equal(DocColumnKind.TextureAsset, loadedTextureColumn!.Kind);
            Assert.Equal(DocColumnKind.MeshAsset, loadedMeshColumn!.Kind);
            Assert.Equal(DocColumnKind.AudioAsset, loadedAudioColumn!.Kind);

            var loadedRow = loadedTable.Rows[0];
            Assert.Equal("Textures/hero.png", loadedRow.GetCell(loadedTextureColumn).StringValue);
            var loadedMeshCell = loadedRow.GetCell(loadedMeshColumn);
            Assert.Equal("Meshes/tree.glb", loadedMeshCell.StringValue);
            Assert.Equal("Audio/voice_hero.mp3", loadedRow.GetCell(loadedAudioColumn!).StringValue);
            Assert.NotNull(loadedMeshCell.ModelPreviewSettings);
            Assert.Equal(-22.5f, loadedMeshCell.ModelPreviewSettings!.OrbitYawDegrees, 3);
            Assert.Equal(14.25f, loadedMeshCell.ModelPreviewSettings!.OrbitPitchDegrees, 3);
            Assert.Equal(0.12f, loadedMeshCell.ModelPreviewSettings!.PanX, 3);
            Assert.Equal(-0.08f, loadedMeshCell.ModelPreviewSettings!.PanY, 3);
            Assert.Equal(1.35f, loadedMeshCell.ModelPreviewSettings!.Zoom, 3);
            Assert.Equal("Textures/bark.png", loadedMeshCell.ModelPreviewSettings!.TextureRelativePath);
        }
        finally
        {
            if (Directory.Exists(tempProjectDirectory))
            {
                Directory.Delete(tempProjectDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ProjectStorage_LoadsLegacyLivePreviewPriority_AsInteractiveScope()
    {
        string tempProjectDirectory = Path.Combine(
            Path.GetTempPath(),
            "DerpDocLegacyPriority_" + Guid.NewGuid().ToString("N"));

        try
        {
            var table = new DocTable { Name = "Metrics", FileName = "metrics" };
            table.Columns.Add(new DocColumn
            {
                Name = "Weight",
                Kind = DocColumnKind.Number,
                FormulaExpression = "1",
                FormulaEvalScopes = DocFormulaEvalScope.Interactive,
            });

            var project = new DocProject
            {
                Name = "LegacyScopeProject",
                Tables = new List<DocTable> { table },
            };

            ProjectSerializer.Save(project, tempProjectDirectory);

            string schemaPath = Path.Combine(tempProjectDirectory, "tables", table.FileName + ".schema.json");
            string schemaJson = File.ReadAllText(schemaPath);
            schemaJson = schemaJson.Replace(
                "\"formulaEvalScopes\":\"Interactive\"",
                "\"livePreviewPriority\":\"ChartImmediate\"",
                StringComparison.Ordinal);
            File.WriteAllText(schemaPath, schemaJson);

            var loadedProject = ProjectLoader.Load(tempProjectDirectory);
            var loadedColumn = loadedProject.Tables[0].Columns[0];
            Assert.Equal(DocFormulaEvalScope.Interactive, loadedColumn.FormulaEvalScopes);
        }
        finally
        {
            if (Directory.Exists(tempProjectDirectory))
            {
                Directory.Delete(tempProjectDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ProjectStorage_RoundTripsEmbeddedTableBlockReference()
    {
        string tempProjectDirectory = Path.Combine(
            Path.GetTempPath(),
            "DerpDocTableBlock_" + Guid.NewGuid().ToString("N"));

        try
        {
            var table = new DocTable { Name = "Tasks", FileName = "tasks" };
            table.Columns.Add(new DocColumn { Name = "Name", Kind = DocColumnKind.Text });

            var document = new DocDocument
            {
                Title = "Doc",
                FileName = "doc"
            };
            document.Blocks.Add(new DocBlock
            {
                Order = "a0",
                Type = DocBlockType.Table,
                TableId = table.Id,
                TableVariantId = 2,
            });

            var project = new DocProject
            {
                Name = "DocBlockStorage",
                Tables = new List<DocTable> { table },
                Documents = new List<DocDocument> { document }
            };

            ProjectSerializer.Save(project, tempProjectDirectory);
            var loadedProject = ProjectLoader.Load(tempProjectDirectory);

            Assert.Single(loadedProject.Documents);
            var loadedBlock = loadedProject.Documents[0].Blocks[0];
            Assert.Equal(DocBlockType.Table, loadedBlock.Type);
            Assert.Equal(table.Id, loadedBlock.TableId);
            Assert.Equal(2, loadedBlock.TableVariantId);
        }
        finally
        {
            if (Directory.Exists(tempProjectDirectory))
            {
                Directory.Delete(tempProjectDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ProjectStorage_RoundTripsEmbeddedTableBlockVariableOverrides()
    {
        string tempProjectDirectory = Path.Combine(
            Path.GetTempPath(),
            "DerpDocTableBlockOverrides_" + Guid.NewGuid().ToString("N"));

        try
        {
            var table = new DocTable { Name = "Tasks", FileName = "tasks" };
            table.Columns.Add(new DocColumn { Name = "Name", Kind = DocColumnKind.Text });
            var firstVariable = new DocTableVariable
            {
                Id = "var_filter_value",
                Name = "filter_value",
                Expression = "\"alpha\"",
            };
            var secondVariable = new DocTableVariable
            {
                Id = "var_sort_desc",
                Name = "sort_desc",
                Kind = DocColumnKind.Checkbox,
                Expression = "false",
            };
            table.Variables.Add(firstVariable);
            table.Variables.Add(secondVariable);

            var document = new DocDocument
            {
                Title = "Doc",
                FileName = "doc"
            };
            document.Blocks.Add(new DocBlock
            {
                Order = "a0",
                Type = DocBlockType.Table,
                TableId = table.Id,
                TableVariableOverrides = new List<DocBlockTableVariableOverride>
                {
                    new()
                    {
                        VariableId = firstVariable.Id,
                        Expression = "\"beta\"",
                    },
                    new()
                    {
                        VariableId = secondVariable.Id,
                        Expression = "true",
                    },
                },
            });

            var project = new DocProject
            {
                Name = "DocBlockOverrideStorage",
                Tables = new List<DocTable> { table },
                Documents = new List<DocDocument> { document }
            };

            ProjectSerializer.Save(project, tempProjectDirectory);
            var loadedProject = ProjectLoader.Load(tempProjectDirectory);

            Assert.Single(loadedProject.Documents);
            var loadedBlock = loadedProject.Documents[0].Blocks[0];
            Assert.Equal(DocBlockType.Table, loadedBlock.Type);
            Assert.Equal(table.Id, loadedBlock.TableId);
            Assert.Equal(2, loadedBlock.TableVariableOverrides.Count);
            Assert.Equal(firstVariable.Id, loadedBlock.TableVariableOverrides[0].VariableId);
            Assert.Equal("\"beta\"", loadedBlock.TableVariableOverrides[0].Expression);
            Assert.Equal(secondVariable.Id, loadedBlock.TableVariableOverrides[1].VariableId);
            Assert.Equal("true", loadedBlock.TableVariableOverrides[1].Expression);
        }
        finally
        {
            if (Directory.Exists(tempProjectDirectory))
            {
                Directory.Delete(tempProjectDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Workspace_SetBlockTableReferenceCommand_IsUndoable()
    {
        var firstTable = new DocTable { Name = "First", FileName = "first" };
        firstTable.Columns.Add(new DocColumn { Name = "Name", Kind = DocColumnKind.Text });
        var secondTable = new DocTable { Name = "Second", FileName = "second" };
        secondTable.Columns.Add(new DocColumn { Name = "Name", Kind = DocColumnKind.Text });

        var document = new DocDocument
        {
            Title = "Doc",
            FileName = "doc",
            Blocks = new List<DocBlock>
            {
                new DocBlock
                {
                    Order = "a0",
                    Type = DocBlockType.Table,
                    TableId = firstTable.Id
                }
            }
        };

        var workspace = CreateIsolatedWorkspace();
        workspace.Project = new DocProject
        {
            Name = "UndoProject",
            Tables = new List<DocTable> { firstTable, secondTable },
            Documents = new List<DocDocument> { document }
        };
        workspace.ActiveDocument = document;
        workspace.ActiveView = ActiveViewKind.Document;

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetBlockTableReference,
            DocumentId = document.Id,
            BlockId = document.Blocks[0].Id,
            OldTableId = firstTable.Id,
            NewTableId = secondTable.Id
        });

        Assert.Equal(secondTable.Id, document.Blocks[0].TableId);

        workspace.Undo();
        Assert.Equal(firstTable.Id, document.Blocks[0].TableId);

        workspace.Redo();
        Assert.Equal(secondTable.Id, document.Blocks[0].TableId);
    }

    [Fact]
    public void Workspace_SetBlockTableVariableOverrideCommand_IsUndoable()
    {
        var table = new DocTable { Name = "Tasks", FileName = "tasks" };
        table.Columns.Add(new DocColumn { Name = "Name", Kind = DocColumnKind.Text });
        var variable = new DocTableVariable
        {
            Name = "filter_value",
            Expression = "\"alpha\"",
        };
        table.Variables.Add(variable);

        var block = new DocBlock
        {
            Order = "a0",
            Type = DocBlockType.Table,
            TableId = table.Id,
        };
        var document = new DocDocument
        {
            Title = "Doc",
            FileName = "doc",
            Blocks = new List<DocBlock> { block },
        };

        var workspace = CreateIsolatedWorkspace();
        workspace.Project = new DocProject
        {
            Name = "UndoBlockVariableOverrideProject",
            Tables = new List<DocTable> { table },
            Documents = new List<DocDocument> { document },
        };
        workspace.ActiveDocument = document;
        workspace.ActiveView = ActiveViewKind.Document;

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetBlockTableVariableOverride,
            TableId = table.Id,
            TableVariableId = variable.Id,
            DocumentId = document.Id,
            BlockId = block.Id,
            OldBlockTableVariableExpression = "",
            NewBlockTableVariableExpression = "\"beta\"",
        });

        Assert.Single(block.TableVariableOverrides);
        Assert.Equal(variable.Id, block.TableVariableOverrides[0].VariableId);
        Assert.Equal("\"beta\"", block.TableVariableOverrides[0].Expression);

        workspace.Undo();
        Assert.Empty(block.TableVariableOverrides);

        workspace.Redo();
        Assert.Single(block.TableVariableOverrides);
        Assert.Equal("\"beta\"", block.TableVariableOverrides[0].Expression);
    }

    [Fact]
    public void Workspace_ComputeViewRowIndices_RefreshesAfterMutation()
    {
        var table = new DocTable { Name = "Tasks", FileName = "tasks" };
        var nameColumn = new DocColumn { Name = "Name", Kind = DocColumnKind.Text };
        table.Columns.Add(nameColumn);

        var rowOne = new DocRow { Id = "row-1" };
        rowOne.SetCell(nameColumn.Id, DocCellValue.Text("b"));
        table.Rows.Add(rowOne);

        var rowTwo = new DocRow { Id = "row-2" };
        rowTwo.SetCell(nameColumn.Id, DocCellValue.Text("a"));
        table.Rows.Add(rowTwo);

        var view = new DocView { Name = "Sorted", Type = DocViewType.Grid };
        view.Sorts.Add(new DocViewSort
        {
            ColumnId = nameColumn.Id,
            Descending = false,
        });
        table.Views.Add(view);

        var workspace = CreateIsolatedWorkspace();
        workspace.Project = new DocProject
        {
            Name = "ViewIndexProject",
            Tables = new List<DocTable> { table },
        };

        var initial = workspace.ComputeViewRowIndices(table, view);
        Assert.NotNull(initial);
        Assert.Equal([1, 0], initial!);

        var repeated = workspace.ComputeViewRowIndices(table, view);
        Assert.Same(initial, repeated);

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetCell,
            TableId = table.Id,
            RowId = rowOne.Id,
            ColumnId = nameColumn.Id,
            OldCellValue = rowOne.GetCell(nameColumn),
            NewCellValue = DocCellValue.Text("0"),
        });

        var refreshed = workspace.ComputeViewRowIndices(table, view);
        Assert.NotNull(refreshed);
        Assert.Equal([0, 1], refreshed!);
    }

    [Fact]
    public void Workspace_PollExternalChanges_RebindsActiveTableView()
    {
        string dbRoot = Path.Combine(Path.GetTempPath(), "derpdoc_reload_view_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dbRoot);

        try
        {
            var table = new DocTable
            {
                Id = "table-main",
                Name = "Main",
                FileName = "main",
            };
            table.Views.Add(new DocView { Id = "view-grid", Name = "Grid", Type = DocViewType.Grid });
            table.Views.Add(new DocView { Id = "view-board", Name = "Board", Type = DocViewType.Board });

            var project = new DocProject { Name = "ReloadProject" };
            project.Tables.Add(table);
            ProjectSerializer.Save(project, dbRoot);

            var workspace = CreateIsolatedWorkspace();
            workspace.LoadProject(dbRoot);
            workspace.ActiveTable = workspace.Project.Tables[0];
            workspace.ActiveTableView = workspace.ActiveTable.Views[1];

            DocExternalChangeSignalFile.Touch(dbRoot);
            for (int pollIndex = 0; pollIndex < 15; pollIndex++)
            {
                workspace.PollExternalChanges();
            }

            Assert.NotNull(workspace.ActiveTable);
            Assert.NotNull(workspace.ActiveTableView);
            Assert.Equal("view-board", workspace.ActiveTableView!.Id);
            Assert.Contains(workspace.ActiveTableView, workspace.ActiveTable!.Views);
        }
        finally
        {
            if (Directory.Exists(dbRoot))
            {
                Directory.Delete(dbRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void FormulaEngine_ResolvesTableVariables_FromThisTableAndTablesNamespace()
    {
        var settingsTable = new DocTable { Name = "Settings", FileName = "settings" };
        settingsTable.Variables.Add(new DocTableVariable
        {
            Name = "limit",
            Expression = "5",
        });
        settingsTable.Columns.Add(new DocColumn { Name = "Name", Kind = DocColumnKind.Text });
        settingsTable.Rows.Add(new DocRow());

        var metricsTable = new DocTable { Name = "Metrics", FileName = "metrics" };
        var localLimitColumn = new DocColumn
        {
            Name = "LocalLimit",
            Kind = DocColumnKind.Number,
            FormulaExpression = "thisTable.limit",
        };
        var globalLimitColumn = new DocColumn
        {
            Name = "GlobalLimit",
            Kind = DocColumnKind.Number,
            FormulaExpression = "tables.Settings.limit",
        };
        metricsTable.Variables.Add(new DocTableVariable
        {
            Name = "limit",
            Expression = "3",
        });
        metricsTable.Columns.Add(localLimitColumn);
        metricsTable.Columns.Add(globalLimitColumn);
        metricsTable.Rows.Add(new DocRow());

        var project = new DocProject
        {
            Name = "TableVariableFormulaProject",
            Tables = new List<DocTable> { settingsTable, metricsTable },
        };

        var formulaEngine = new DocFormulaEngine();
        formulaEngine.EvaluateProject(project);

        Assert.Equal(3, metricsTable.Rows[0].GetCell(localLimitColumn).NumberValue);
        Assert.Equal(5, metricsTable.Rows[0].GetCell(globalLimitColumn).NumberValue);
    }

    [Fact]
    public void FormulaEngine_Incremental_ReevaluatesTableFormula_WhenDependencyComesFromTableVariableExpression()
    {
        var settingsTable = new DocTable { Name = "Settings", FileName = "settings" };
        settingsTable.Variables.Add(new DocTableVariable
        {
            Name = "limit",
            Expression = "5",
        });
        settingsTable.Rows.Add(new DocRow());

        var metricsTable = new DocTable { Name = "Metrics", FileName = "metrics" };
        metricsTable.Variables.Add(new DocTableVariable
        {
            Name = "effective_limit",
            Expression = "tables.Settings.limit",
        });
        var limitColumn = new DocColumn
        {
            Name = "Limit",
            Kind = DocColumnKind.Number,
            FormulaExpression = "thisTable.effective_limit",
        };
        metricsTable.Columns.Add(limitColumn);
        var metricsRow = new DocRow();
        metricsTable.Rows.Add(metricsRow);

        var project = new DocProject
        {
            Name = "TableVariableIncrementalDependenciesProject",
            Tables = new List<DocTable> { settingsTable, metricsTable },
        };

        var formulaEngine = new DocFormulaEngine();
        formulaEngine.EvaluateProject(project, DocFormulaEngine.EvaluationRequest.Full());
        Assert.Equal(5, metricsRow.GetCell(limitColumn).NumberValue);

        settingsTable.Variables[0].Expression = "7";
        var metrics = formulaEngine.EvaluateProject(
            project,
            DocFormulaEngine.EvaluationRequest.Incremental(
                new[] { settingsTable.Id },
                refreshDirtyTableIndexes: true));

        Assert.True(metrics.UsedIncrementalPlan);
        Assert.Equal(7, metricsRow.GetCell(limitColumn).NumberValue);
    }

    [Fact]
    public void Workspace_TableVariableExpressionCommands_UseIncrementalFormulaRefresh_AfterWarmup()
    {
        var table = new DocTable { Name = "Metrics", FileName = "metrics" };
        var limitVariable = new DocTableVariable
        {
            Name = "limit",
            Expression = "1",
        };
        table.Variables.Add(limitVariable);
        var limitColumn = new DocColumn
        {
            Name = "Limit",
            Kind = DocColumnKind.Number,
            FormulaExpression = "thisTable.limit",
        };
        table.Columns.Add(limitColumn);
        var row = new DocRow();
        table.Rows.Add(row);

        var workspace = CreateIsolatedWorkspace();
        workspace.Project = new DocProject
        {
            Name = "VariableIncrementalWorkspaceProject",
            Tables = new List<DocTable> { table },
        };
        workspace.ActiveTable = table;

        // Warm the dependency plan. First edit can be full if the plan has not been built yet.
        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetTableVariableExpression,
            TableId = table.Id,
            TableVariableId = limitVariable.Id,
            OldTableVariableExpression = "1",
            NewTableVariableExpression = "2",
        });

        var perfAfterWarmup = workspace.GetPerformanceCounters();
        long incrementalCountBeforeSecondEdit = perfAfterWarmup.FormulaIncrementalCount;
        long fullCountBeforeSecondEdit = perfAfterWarmup.FormulaFullCount;

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetTableVariableExpression,
            TableId = table.Id,
            TableVariableId = limitVariable.Id,
            OldTableVariableExpression = "2",
            NewTableVariableExpression = "3",
        });

        var perfAfterSecondEdit = workspace.GetPerformanceCounters();
        Assert.True(perfAfterSecondEdit.FormulaIncrementalCount > incrementalCountBeforeSecondEdit);
        Assert.Equal(fullCountBeforeSecondEdit, perfAfterSecondEdit.FormulaFullCount);
        Assert.Equal(3, row.GetCell(limitColumn).NumberValue);
    }

    [Fact]
    public void ProjectStorage_RoundTripsTableVariables_AndViewBindings()
    {
        string tempProjectDirectory = Path.Combine(
            Path.GetTempPath(),
            "DerpDocTableVariableStorage_" + Guid.NewGuid().ToString("N"));

        try
        {
            var table = new DocTable
            {
                Name = "Tasks",
                FileName = "tasks",
            };
            var statusColumn = new DocColumn { Name = "Status", Kind = DocColumnKind.Select, Options = new List<string> { "Todo", "Done" } };
            var titleColumn = new DocColumn { Name = "Title", Kind = DocColumnKind.Text };
            table.Columns.Add(statusColumn);
            table.Columns.Add(titleColumn);

            table.Variables.Add(new DocTableVariable
            {
                Id = "var_status_column",
                Name = "status_column",
                Kind = DocColumnKind.Text,
                Expression = "\"" + statusColumn.Id + "\"",
            });
            table.Variables.Add(new DocTableVariable
            {
                Id = "var_filter_value",
                Name = "filter_value",
                Kind = DocColumnKind.Select,
                Expression = "\"Done\"",
            });

            var view = new DocView
            {
                Id = "view_board",
                Name = "Board",
                Type = DocViewType.Board,
                GroupByColumnId = statusColumn.Id,
                GroupByColumnBinding = new DocViewBinding
                {
                    VariableName = "status_column",
                },
            };
            view.Sorts.Add(new DocViewSort
            {
                Id = "sort_title",
                ColumnId = titleColumn.Id,
                Descending = false,
                ColumnIdBinding = new DocViewBinding { VariableName = "status_column", FormulaExpression = "\""+titleColumn.Id+"\"" },
                DescendingBinding = new DocViewBinding { FormulaExpression = "true" },
            });
            view.Filters.Add(new DocViewFilter
            {
                Id = "filter_status",
                ColumnId = statusColumn.Id,
                Op = DocViewFilterOp.Equals,
                Value = "Done",
                ColumnIdBinding = new DocViewBinding { VariableName = "status_column" },
                OpBinding = new DocViewBinding { FormulaExpression = "\"Equals\"" },
                ValueBinding = new DocViewBinding { VariableName = "filter_value" },
            });
            table.Views.Add(view);

            var project = new DocProject
            {
                Name = "BindingStorageProject",
                Tables = new List<DocTable> { table },
            };

            ProjectSerializer.Save(project, tempProjectDirectory);
            var loadedProject = ProjectLoader.Load(tempProjectDirectory);
            var loadedTable = loadedProject.Tables[0];
            Assert.Equal(2, loadedTable.Variables.Count);
            Assert.Equal("status_column", loadedTable.Variables[0].Name);
            Assert.Equal(DocColumnKind.Text, loadedTable.Variables[0].Kind);
            Assert.Equal(DocColumnKind.Select, loadedTable.Variables[1].Kind);

            var loadedView = loadedTable.Views[0];
            Assert.NotNull(loadedView.GroupByColumnBinding);
            Assert.Equal("status_column", loadedView.GroupByColumnBinding!.VariableName);
            Assert.Single(loadedView.Sorts);
            Assert.Single(loadedView.Filters);
            Assert.Equal("sort_title", loadedView.Sorts[0].Id);
            Assert.NotNull(loadedView.Sorts[0].DescendingBinding);
            Assert.Equal("true", loadedView.Sorts[0].DescendingBinding!.FormulaExpression);
            Assert.Equal("filter_status", loadedView.Filters[0].Id);
            Assert.NotNull(loadedView.Filters[0].ValueBinding);
            Assert.Equal("filter_value", loadedView.Filters[0].ValueBinding!.VariableName);
        }
        finally
        {
            if (Directory.Exists(tempProjectDirectory))
            {
                Directory.Delete(tempProjectDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ProjectStorage_RoundTripsTableVariablePluginTypeId()
    {
        string tempProjectDirectory = Path.Combine(
            Path.GetTempPath(),
            "DerpDocTableVariablePluginTypeStorage_" + Guid.NewGuid().ToString("N"));

        try
        {
            var table = new DocTable
            {
                Name = "Metrics",
                FileName = "metrics",
            };
            table.Variables.Add(new DocTableVariable
            {
                Id = "var_custom_metric",
                Name = "custom_metric",
                Kind = DocColumnKind.Number,
                ColumnTypeId = "plugin.custom_metric",
                Expression = "42",
            });

            var project = new DocProject
            {
                Name = "PluginTypeStorageProject",
                Tables = new List<DocTable> { table },
            };

            ProjectSerializer.Save(project, tempProjectDirectory);
            var loadedProject = ProjectLoader.Load(tempProjectDirectory);
            var loadedVariable = loadedProject.Tables[0].Variables[0];
            Assert.Equal(DocColumnKind.Number, loadedVariable.Kind);
            Assert.Equal("plugin.custom_metric", loadedVariable.ColumnTypeId);
        }
        finally
        {
            if (Directory.Exists(tempProjectDirectory))
            {
                Directory.Delete(tempProjectDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Workspace_ResolveViewConfig_AppliesBoundFilterAndSortValues()
    {
        var table = new DocTable { Name = "Tasks", FileName = "tasks" };
        var titleColumn = new DocColumn { Name = "Title", Kind = DocColumnKind.Text };
        table.Columns.Add(titleColumn);

        var rowAlpha = new DocRow { Id = "row-alpha" };
        rowAlpha.SetCell(titleColumn.Id, DocCellValue.Text("alpha"));
        table.Rows.Add(rowAlpha);

        var rowBeta = new DocRow { Id = "row-beta" };
        rowBeta.SetCell(titleColumn.Id, DocCellValue.Text("beta"));
        table.Rows.Add(rowBeta);

        var rowZulu = new DocRow { Id = "row-zulu" };
        rowZulu.SetCell(titleColumn.Id, DocCellValue.Text("zulu"));
        table.Rows.Add(rowZulu);

        table.Variables.Add(new DocTableVariable
        {
            Name = "filter_column",
            Expression = "\"" + titleColumn.Id + "\"",
        });
        table.Variables.Add(new DocTableVariable
        {
            Name = "filter_value",
            Expression = "\"a\"",
        });
        table.Variables.Add(new DocTableVariable
        {
            Name = "filter_op",
            Expression = "\"Contains\"",
        });
        table.Variables.Add(new DocTableVariable
        {
            Name = "sort_column",
            Expression = "\"" + titleColumn.Id + "\"",
        });
        table.Variables.Add(new DocTableVariable
        {
            Name = "sort_descending",
            Kind = DocColumnKind.Checkbox,
            Expression = "true",
        });

        var view = new DocView { Name = "Bound", Type = DocViewType.Grid };
        view.Filters.Add(new DocViewFilter
        {
            ColumnId = "",
            Op = DocViewFilterOp.Equals,
            Value = "",
            ColumnIdBinding = new DocViewBinding { VariableName = "filter_column" },
            OpBinding = new DocViewBinding { VariableName = "filter_op" },
            ValueBinding = new DocViewBinding { VariableName = "filter_value" },
        });
        view.Sorts.Add(new DocViewSort
        {
            ColumnId = "",
            Descending = false,
            ColumnIdBinding = new DocViewBinding { VariableName = "sort_column" },
            DescendingBinding = new DocViewBinding { VariableName = "sort_descending" },
        });
        table.Views.Add(view);

        var workspace = CreateIsolatedWorkspace();
        workspace.Project = new DocProject
        {
            Name = "ResolveBindingsProject",
            Tables = new List<DocTable> { table },
        };

        var resolvedView = workspace.ResolveViewConfig(table, view);
        Assert.NotNull(resolvedView);
        Assert.Equal(titleColumn.Id, resolvedView!.Filters[0].ColumnId);
        Assert.Equal(DocViewFilterOp.Contains, resolvedView.Filters[0].Op);
        Assert.Equal("a", resolvedView.Filters[0].Value);
        Assert.Equal(titleColumn.Id, resolvedView.Sorts[0].ColumnId);
        Assert.True(resolvedView.Sorts[0].Descending);

        var rowIndices = workspace.ComputeViewRowIndices(table, view);
        Assert.NotNull(rowIndices);
        Assert.Equal([1, 0], rowIndices!);
    }

    [Fact]
    public void Workspace_ResolveViewConfig_UsesBlockTableVariableOverrides()
    {
        var table = new DocTable { Name = "Tasks", FileName = "tasks" };
        var titleColumn = new DocColumn { Name = "Title", Kind = DocColumnKind.Text };
        table.Columns.Add(titleColumn);

        var alphaRow = new DocRow { Id = "row-alpha" };
        alphaRow.SetCell(titleColumn.Id, DocCellValue.Text("alpha"));
        table.Rows.Add(alphaRow);

        var betaRow = new DocRow { Id = "row-beta" };
        betaRow.SetCell(titleColumn.Id, DocCellValue.Text("beta"));
        table.Rows.Add(betaRow);

        var filterColumnVariable = new DocTableVariable
        {
            Id = "var_filter_column",
            Name = "filter_column",
            Expression = "\"" + titleColumn.Id + "\"",
        };
        var filterOperatorVariable = new DocTableVariable
        {
            Id = "var_filter_operator",
            Name = "filter_operator",
            Expression = "\"Equals\"",
        };
        var filterValueVariable = new DocTableVariable
        {
            Id = "var_filter_value",
            Name = "filter_value",
            Expression = "\"alpha\"",
        };
        table.Variables.Add(filterColumnVariable);
        table.Variables.Add(filterOperatorVariable);
        table.Variables.Add(filterValueVariable);

        var view = new DocView { Name = "Bound", Type = DocViewType.Grid };
        view.Filters.Add(new DocViewFilter
        {
            ColumnIdBinding = new DocViewBinding { VariableName = filterColumnVariable.Name },
            OpBinding = new DocViewBinding { VariableName = filterOperatorVariable.Name },
            ValueBinding = new DocViewBinding { VariableName = filterValueVariable.Name },
        });
        table.Views.Add(view);

        var firstBlock = new DocBlock
        {
            Type = DocBlockType.Table,
            TableId = table.Id,
            Order = "a0",
        };
        var secondBlock = new DocBlock
        {
            Type = DocBlockType.Table,
            TableId = table.Id,
            Order = "a1",
            TableVariableOverrides = new List<DocBlockTableVariableOverride>
            {
                new()
                {
                    VariableId = filterValueVariable.Id,
                    Expression = "\"beta\"",
                }
            },
        };

        var workspace = CreateIsolatedWorkspace();
        workspace.Project = new DocProject
        {
            Name = "ResolveBlockOverridesProject",
            Tables = new List<DocTable> { table },
            Documents = new List<DocDocument>
            {
                new DocDocument
                {
                    Title = "Doc",
                    FileName = "doc",
                    Blocks = new List<DocBlock> { firstBlock, secondBlock },
                }
            },
        };

        var firstResolvedView = workspace.ResolveViewConfig(table, view, firstBlock);
        Assert.NotNull(firstResolvedView);
        Assert.Equal("alpha", firstResolvedView!.Filters[0].Value);

        var secondResolvedView = workspace.ResolveViewConfig(table, view, secondBlock);
        Assert.NotNull(secondResolvedView);
        Assert.Equal("beta", secondResolvedView!.Filters[0].Value);

        var firstRowIndices = workspace.ComputeViewRowIndices(table, view, firstBlock);
        Assert.NotNull(firstRowIndices);
        Assert.Equal([0], firstRowIndices!);

        var secondRowIndices = workspace.ComputeViewRowIndices(table, view, secondBlock);
        Assert.NotNull(secondRowIndices);
        Assert.Equal([1], secondRowIndices!);
    }

    [Fact]
    public void Workspace_SetBlockTableVariableOverride_UpdatesInstanceWithoutMutatingTableDefaults()
    {
        var table = new DocTable { Name = "Tasks", FileName = "tasks" };
        var titleColumn = new DocColumn { Name = "Title", Kind = DocColumnKind.Text };
        table.Columns.Add(titleColumn);

        var alphaRow = new DocRow { Id = "row-alpha" };
        alphaRow.SetCell(titleColumn.Id, DocCellValue.Text("alpha"));
        table.Rows.Add(alphaRow);

        var betaRow = new DocRow { Id = "row-beta" };
        betaRow.SetCell(titleColumn.Id, DocCellValue.Text("beta"));
        table.Rows.Add(betaRow);

        var filterColumnVariable = new DocTableVariable
        {
            Id = "var_filter_column",
            Name = "filter_column",
            Expression = "\"" + titleColumn.Id + "\"",
        };
        var filterOperatorVariable = new DocTableVariable
        {
            Id = "var_filter_operator",
            Name = "filter_operator",
            Expression = "\"Equals\"",
        };
        var filterValueVariable = new DocTableVariable
        {
            Id = "var_filter_value",
            Name = "filter_value",
            Expression = "\"alpha\"",
        };
        table.Variables.Add(filterColumnVariable);
        table.Variables.Add(filterOperatorVariable);
        table.Variables.Add(filterValueVariable);

        var view = new DocView { Name = "Bound", Type = DocViewType.Grid };
        view.Filters.Add(new DocViewFilter
        {
            ColumnIdBinding = new DocViewBinding { VariableName = filterColumnVariable.Name },
            OpBinding = new DocViewBinding { VariableName = filterOperatorVariable.Name },
            ValueBinding = new DocViewBinding { VariableName = filterValueVariable.Name },
        });
        table.Views.Add(view);

        var tableBlock = new DocBlock
        {
            Type = DocBlockType.Table,
            TableId = table.Id,
            Order = "a0",
        };
        var document = new DocDocument
        {
            Id = "doc-1",
            Title = "Doc",
            FileName = "doc",
            Blocks = new List<DocBlock> { tableBlock },
        };

        var workspace = CreateIsolatedWorkspace();
        workspace.Project = new DocProject
        {
            Name = "SetBlockOverrideProject",
            Tables = new List<DocTable> { table },
            Documents = new List<DocDocument> { document },
        };

        var initialResolvedView = workspace.ResolveViewConfig(table, view, tableBlock);
        Assert.NotNull(initialResolvedView);
        Assert.Equal("alpha", initialResolvedView!.Filters[0].Value);

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetBlockTableVariableOverride,
            TableId = table.Id,
            TableVariableId = filterValueVariable.Id,
            DocumentId = document.Id,
            BlockId = tableBlock.Id,
            OldBlockTableVariableExpression = "",
            NewBlockTableVariableExpression = "\"beta\"",
        });

        Assert.Equal("\"alpha\"", filterValueVariable.Expression);

        var updatedResolvedView = workspace.ResolveViewConfig(table, view, tableBlock);
        Assert.NotNull(updatedResolvedView);
        Assert.Equal("beta", updatedResolvedView!.Filters[0].Value);

        var updatedRowIndices = workspace.ComputeViewRowIndices(table, view, tableBlock);
        Assert.NotNull(updatedRowIndices);
        Assert.Equal([1], updatedRowIndices!);

        var defaultResolvedView = workspace.ResolveViewConfig(table, view, tableInstanceBlock: null);
        Assert.NotNull(defaultResolvedView);
        Assert.Equal("alpha", defaultResolvedView!.Filters[0].Value);
    }

    [Fact]
    public void RemoveTableVariableCommand_ClearsViewBindingVariableReferences()
    {
        var table = new DocTable { Name = "Tasks", FileName = "tasks" };
        var statusColumn = new DocColumn { Name = "Status", Kind = DocColumnKind.Select, Options = new List<string> { "Todo", "Done" } };
        table.Columns.Add(statusColumn);

        var tableVariable = new DocTableVariable
        {
            Name = "status_column",
            Expression = "\"" + statusColumn.Id + "\"",
        };
        table.Variables.Add(tableVariable);

        var view = new DocView
        {
            Name = "Board",
            Type = DocViewType.Board,
            GroupByColumnBinding = new DocViewBinding { VariableName = tableVariable.Name },
        };
        view.Filters.Add(new DocViewFilter
        {
            ColumnIdBinding = new DocViewBinding { VariableName = tableVariable.Name },
            OpBinding = new DocViewBinding { VariableName = tableVariable.Name },
            ValueBinding = new DocViewBinding { VariableName = tableVariable.Name },
        });
        view.Sorts.Add(new DocViewSort
        {
            ColumnIdBinding = new DocViewBinding { VariableName = tableVariable.Name },
            DescendingBinding = new DocViewBinding { VariableName = tableVariable.Name },
        });
        table.Views.Add(view);

        var workspace = CreateIsolatedWorkspace();
        workspace.Project = new DocProject
        {
            Name = "RemoveVariableProject",
            Tables = new List<DocTable> { table },
        };

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.RemoveTableVariable,
            TableId = table.Id,
            TableVariableId = tableVariable.Id,
            TableVariableIndex = 0,
            TableVariableSnapshot = tableVariable.Clone(),
        });

        Assert.Empty(table.Variables);
        Assert.True(string.IsNullOrWhiteSpace(view.GroupByColumnBinding?.VariableName));
        Assert.True(string.IsNullOrWhiteSpace(view.Filters[0].ColumnIdBinding?.VariableName));
        Assert.True(string.IsNullOrWhiteSpace(view.Filters[0].OpBinding?.VariableName));
        Assert.True(string.IsNullOrWhiteSpace(view.Filters[0].ValueBinding?.VariableName));
        Assert.True(string.IsNullOrWhiteSpace(view.Sorts[0].ColumnIdBinding?.VariableName));
        Assert.True(string.IsNullOrWhiteSpace(view.Sorts[0].DescendingBinding?.VariableName));
    }

    private static DocRow CreateTaskRow(
        DocColumn taskNameColumn,
        DocColumn assigneeColumn,
        DocColumn hoursColumn,
        DocColumn doneColumn,
        string taskName,
        string assigneeName,
        double taskHours,
        bool isDone)
    {
        var taskRow = new DocRow();
        taskRow.SetCell(taskNameColumn.Id, DocCellValue.Text(taskName));
        taskRow.SetCell(assigneeColumn.Id, DocCellValue.Text(assigneeName));
        taskRow.SetCell(hoursColumn.Id, DocCellValue.Number(taskHours));
        taskRow.SetCell(doneColumn.Id, DocCellValue.Bool(isDone));
        return taskRow;
    }

    private static DocRow CreatePersonRow(DocColumn peopleNameColumn, string personName)
    {
        var personRow = new DocRow();
        personRow.SetCell(peopleNameColumn.Id, DocCellValue.Text(personName));
        return personRow;
    }

    private static DocProject BuildParentScopeFormulaProject(
        out DocTable parentTable,
        out DocRow parentRow,
        out DocColumn baseWeightColumn,
        out DocTable childTable,
        out DocColumn levelColumn,
        out DocColumn weightColumn,
        out DocColumn parentCountColumn,
        out DocRow childRow)
    {
        parentTable = new DocTable
        {
            Name = "EnemyTypes",
            FileName = "enemy_types",
        };
        var enemyTypeColumn = new DocColumn { Name = "EnemyType", Kind = DocColumnKind.Text };
        baseWeightColumn = new DocColumn { Name = "BaseWeight", Kind = DocColumnKind.Number };
        parentTable.Columns.Add(enemyTypeColumn);
        parentTable.Columns.Add(baseWeightColumn);

        parentRow = new DocRow { Id = "enemy_basic" };
        parentRow.SetCell(enemyTypeColumn.Id, DocCellValue.Text("BASIC"));
        parentRow.SetCell(baseWeightColumn.Id, DocCellValue.Number(2));
        parentTable.Rows.Add(parentRow);

        childTable = new DocTable
        {
            Name = "EnemyTypes_SpawnCurve",
            FileName = "enemy_types_spawn_curve",
            ParentTableId = parentTable.Id,
        };
        var parentRowIdColumn = new DocColumn { Name = "_parentRowId", Kind = DocColumnKind.Text, IsHidden = true };
        childTable.ParentRowColumnId = parentRowIdColumn.Id;
        levelColumn = new DocColumn { Name = "Level", Kind = DocColumnKind.Number };
        weightColumn = new DocColumn
        {
            Name = "Weight",
            Kind = DocColumnKind.Number,
            FormulaExpression = "parentRow.BaseWeight + thisRow.Level",
        };
        parentCountColumn = new DocColumn
        {
            Name = "ParentCount",
            Kind = DocColumnKind.Number,
            FormulaExpression = "parentTable.Filter(@EnemyType == parentRow.EnemyType).Count()",
        };
        childTable.Columns.Add(parentRowIdColumn);
        childTable.Columns.Add(levelColumn);
        childTable.Columns.Add(weightColumn);
        childTable.Columns.Add(parentCountColumn);

        childRow = new DocRow { Id = "curve_basic_001" };
        childRow.SetCell(parentRowIdColumn.Id, DocCellValue.Text(parentRow.Id));
        childRow.SetCell(levelColumn.Id, DocCellValue.Number(3));
        childTable.Rows.Add(childRow);

        var project = new DocProject { Name = "ParentScopeProject" };
        project.Tables.Add(parentTable);
        project.Tables.Add(childTable);
        return project;
    }

    private static DocWorkspace CreateIsolatedWorkspace()
    {
        var workspace = new DocWorkspace();
        workspace.AutoSave = false;
        workspace.ProjectPath = null;
        return workspace;
    }
}
