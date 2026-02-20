using System.Reflection;
using Derp.Doc.Editor;
using Derp.Doc.Model;

namespace Derp.Doc.Tests;

public sealed class SpreadsheetFormulaCompletionTests
{
    private static readonly Type SpreadsheetRendererType =
        typeof(DocWorkspace).Assembly.GetType("Derp.Doc.Panels.SpreadsheetRenderer")
        ?? throw new InvalidOperationException("SpreadsheetRenderer type not found.");

    [Fact]
    public void Completion_OffersRowColumns_AfterFirstInChain()
    {
        var workspace = CreateWorkspaceWithPeopleAndRecipes(out var recipesTable, out _);

        var completions = GetCompletionDisplayTexts(
            workspace,
            recipesTable,
            "Recipes.Filter(@Done).First().");

        Assert.Contains("Name", completions);
        Assert.Contains("Calories", completions);
        Assert.DoesNotContain(completions, entry => entry.StartsWith("Filter(", StringComparison.Ordinal));
    }

    [Fact]
    public void Completion_OffersCollectionMethods_AfterFilterInChain()
    {
        var workspace = CreateWorkspaceWithPeopleAndRecipes(out var recipesTable, out _);

        var completions = GetCompletionDisplayTexts(
            workspace,
            recipesTable,
            "Recipes.Filter(@Done).");

        Assert.Contains(completions, entry => entry.StartsWith("First(", StringComparison.Ordinal));
        Assert.Contains(completions, entry => entry.StartsWith("Count(", StringComparison.Ordinal));
        Assert.DoesNotContain("Name", completions);
    }

    [Fact]
    public void Completion_OffersRelationTargetColumns_AfterRelationMemberDot()
    {
        var workspace = CreateWorkspaceWithPeopleAndRecipes(out var recipesTable, out _);

        var completions = GetCompletionDisplayTexts(
            workspace,
            recipesTable,
            "thisRow.Author.");

        Assert.Contains("DisplayName", completions);
        Assert.Contains("Priority", completions);
        Assert.Contains("rowIndex", completions);
        Assert.DoesNotContain(completions, entry => entry.StartsWith("Filter(", StringComparison.Ordinal));
    }

    [Fact]
    public void Completion_OffersThisRowIndexKeyword()
    {
        var workspace = CreateWorkspaceWithPeopleAndRecipes(out var recipesTable, out _);

        var completions = GetCompletionDisplayTexts(
            workspace,
            recipesTable,
            "thisRowI");

        Assert.Contains("thisRowIndex", completions);
    }

    [Fact]
    public void Completion_OffersAtRowIndexInPredicateContext()
    {
        var workspace = CreateWorkspaceWithPeopleAndRecipes(out var recipesTable, out _);

        var completions = GetCompletionDisplayTexts(
            workspace,
            recipesTable,
            "Lookup(Recipes, @r");

        Assert.Contains("@rowIndex", completions);
    }

    [Fact]
    public void Completion_OffersExpFunction()
    {
        var workspace = CreateWorkspaceWithPeopleAndRecipes(out var recipesTable, out _);

        var completions = GetCompletionDisplayTexts(
            workspace,
            recipesTable,
            "Ex");

        Assert.Contains(completions, entry => entry.StartsWith("Exp(", StringComparison.Ordinal));
    }

    [Fact]
    public void Completion_OffersPowAndAbsFunctions()
    {
        var workspace = CreateWorkspaceWithPeopleAndRecipes(out var recipesTable, out _);

        var completions = GetCompletionDisplayTexts(
            workspace,
            recipesTable,
            "Po");

        Assert.Contains(completions, entry => entry.StartsWith("Pow(", StringComparison.Ordinal));

        completions = GetCompletionDisplayTexts(
            workspace,
            recipesTable,
            "Ab");

        Assert.Contains(completions, entry => entry.StartsWith("Abs(", StringComparison.Ordinal));
    }

    [Fact]
    public void Completion_OffersEvalSplineFunction()
    {
        var workspace = CreateWorkspaceWithPeopleAndRecipes(out var recipesTable, out _);

        var completions = GetCompletionDisplayTexts(
            workspace,
            recipesTable,
            "EvalS");

        Assert.Contains(completions, entry => entry.StartsWith("EvalSpline(", StringComparison.Ordinal));
    }

    [Fact]
    public void Completion_OffersParentScopeKeywords_AndMembers_ForSubtable()
    {
        var workspace = CreateWorkspaceWithSubtableContext(out var childTable);

        var keywordCompletions = GetCompletionDisplayTexts(workspace, childTable, "parent");
        Assert.Contains("parentRow", keywordCompletions);
        Assert.Contains("parentTable", keywordCompletions);

        var parentRowCompletions = GetCompletionDisplayTexts(workspace, childTable, "parentRow.");
        Assert.Contains("BaseWeight", parentRowCompletions);
        Assert.Contains("EnemyType", parentRowCompletions);
        Assert.Contains("rowIndex", parentRowCompletions);

        var parentTableCompletions = GetCompletionDisplayTexts(workspace, childTable, "parentTable.");
        Assert.Contains(parentTableCompletions, entry => entry.StartsWith("Filter(", StringComparison.Ordinal));
        Assert.Contains(parentTableCompletions, entry => entry.StartsWith("Count(", StringComparison.Ordinal));
    }

    [Fact]
    public void Completion_OffersDocsKeyword_AndDocumentAliases()
    {
        var workspace = CreateWorkspaceWithDocumentVariables(out var table);

        var keywordCompletions = GetCompletionDisplayTexts(workspace, table, "do");
        Assert.Contains("docs", keywordCompletions);

        var aliasCompletions = GetCompletionDisplayTexts(workspace, table, "docs.");
        Assert.Contains("balance_sheet", aliasCompletions);
    }

    [Fact]
    public void Completion_OffersDocumentVariables_AfterDocumentAliasDot()
    {
        var workspace = CreateWorkspaceWithDocumentVariables(out var table);

        var variableCompletions = GetCompletionDisplayTexts(workspace, table, "docs.balance_sheet.");
        Assert.Contains("tax_rate", variableCompletions);
        Assert.Contains("gross_margin", variableCompletions);
    }

    private static List<string> GetCompletionDisplayTexts(
        DocWorkspace workspace,
        DocTable activeTable,
        string expression)
    {
        MethodInfo buildCompletionsMethod = SpreadsheetRendererType.GetMethod(
            "BuildFormulaCompletionEntries",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("BuildFormulaCompletionEntries method not found.");

        char[] buffer = expression.ToCharArray();
        int length = buffer.Length;
        object[] args = [workspace, activeTable, true, buffer, length, length, 0, 0];
        buildCompletionsMethod.Invoke(null, args);

        FieldInfo completionCountField = SpreadsheetRendererType.GetField(
            "_formulaCompletionCount",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("_formulaCompletionCount field not found.");

        FieldInfo completionEntriesField = SpreadsheetRendererType.GetField(
            "_formulaCompletionEntries",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("_formulaCompletionEntries field not found.");

        int completionCount = (int)(completionCountField.GetValue(null) ?? 0);
        Array entries = (Array)(completionEntriesField.GetValue(null)
            ?? throw new InvalidOperationException("Completion entries array was null."));

        if (completionCount <= 0)
        {
            return [];
        }

        object? firstEntry = entries.GetValue(0);
        if (firstEntry == null)
        {
            return [];
        }

        FieldInfo displayTextField = firstEntry.GetType().GetField(
            "DisplayText",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("DisplayText field not found.");

        var displayTexts = new List<string>(completionCount);
        for (int index = 0; index < completionCount; index++)
        {
            object? entry = entries.GetValue(index);
            if (entry == null)
            {
                continue;
            }

            string? displayText = displayTextField.GetValue(entry) as string;
            if (!string.IsNullOrEmpty(displayText))
            {
                displayTexts.Add(displayText);
            }
        }

        return displayTexts;
    }

    private static DocWorkspace CreateWorkspaceWithPeopleAndRecipes(
        out DocTable recipesTable,
        out DocTable peopleTable)
    {
        peopleTable = new DocTable { Name = "People", FileName = "people" };
        var displayNameColumn = new DocColumn { Name = "DisplayName", Kind = DocColumnKind.Text };
        var priorityColumn = new DocColumn { Name = "Priority", Kind = DocColumnKind.Number };
        peopleTable.Columns.Add(displayNameColumn);
        peopleTable.Columns.Add(priorityColumn);

        var personRow = new DocRow();
        personRow.SetCell(displayNameColumn.Id, DocCellValue.Text("Alice"));
        personRow.SetCell(priorityColumn.Id, DocCellValue.Number(3));
        peopleTable.Rows.Add(personRow);

        recipesTable = new DocTable { Name = "Recipes", FileName = "recipes" };
        var nameColumn = new DocColumn { Name = "Name", Kind = DocColumnKind.Text };
        var caloriesColumn = new DocColumn { Name = "Calories", Kind = DocColumnKind.Number };
        var doneColumn = new DocColumn { Name = "Done", Kind = DocColumnKind.Checkbox };
        var authorColumn = new DocColumn
        {
            Name = "Author",
            Kind = DocColumnKind.Relation,
            RelationTableId = peopleTable.Id
        };
        recipesTable.Columns.Add(nameColumn);
        recipesTable.Columns.Add(caloriesColumn);
        recipesTable.Columns.Add(doneColumn);
        recipesTable.Columns.Add(authorColumn);

        var recipeRow = new DocRow();
        recipeRow.SetCell(nameColumn.Id, DocCellValue.Text("Brownies"));
        recipeRow.SetCell(caloriesColumn.Id, DocCellValue.Number(420));
        recipeRow.SetCell(doneColumn.Id, DocCellValue.Bool(true));
        recipeRow.SetCell(authorColumn.Id, DocCellValue.Text(personRow.Id));
        recipesTable.Rows.Add(recipeRow);

        var workspace = CreateIsolatedWorkspace();
        workspace.Project = new DocProject
        {
            Name = "CompletionProject",
            Tables = new List<DocTable> { peopleTable, recipesTable }
        };
        workspace.ActiveTable = recipesTable;
        return workspace;
    }

    private static DocWorkspace CreateWorkspaceWithSubtableContext(out DocTable childTable)
    {
        var parentTable = new DocTable { Name = "EnemyTypes", FileName = "enemy_types" };
        var enemyTypeColumn = new DocColumn { Name = "EnemyType", Kind = DocColumnKind.Text };
        var baseWeightColumn = new DocColumn { Name = "BaseWeight", Kind = DocColumnKind.Number };
        parentTable.Columns.Add(enemyTypeColumn);
        parentTable.Columns.Add(baseWeightColumn);

        var parentRow = new DocRow();
        parentRow.SetCell(enemyTypeColumn.Id, DocCellValue.Text("BASIC"));
        parentRow.SetCell(baseWeightColumn.Id, DocCellValue.Number(2));
        parentTable.Rows.Add(parentRow);

        childTable = new DocTable
        {
            Name = "EnemyTypes_SpawnCurve",
            FileName = "enemy_types_spawn_curve",
            ParentTableId = parentTable.Id
        };
        var parentRowIdColumn = new DocColumn { Name = "_parentRowId", Kind = DocColumnKind.Text };
        childTable.ParentRowColumnId = parentRowIdColumn.Id;
        childTable.Columns.Add(parentRowIdColumn);
        childTable.Columns.Add(new DocColumn { Name = "Level", Kind = DocColumnKind.Number });

        var childRow = new DocRow();
        childRow.SetCell(parentRowIdColumn.Id, DocCellValue.Text(parentRow.Id));
        childTable.Rows.Add(childRow);

        var workspace = CreateIsolatedWorkspace();
        workspace.Project = new DocProject
        {
            Name = "ParentCompletionProject",
            Tables = new List<DocTable> { parentTable, childTable }
        };
        workspace.ActiveTable = childTable;
        return workspace;
    }

    private static DocWorkspace CreateIsolatedWorkspace()
    {
        var workspace = new DocWorkspace();
        workspace.AutoSave = false;
        workspace.ProjectPath = null;
        return workspace;
    }

    private static DocWorkspace CreateWorkspaceWithDocumentVariables(out DocTable table)
    {
        table = new DocTable { Name = "Metrics", FileName = "metrics" };
        table.Columns.Add(new DocColumn { Name = "Value", Kind = DocColumnKind.Number });
        table.Rows.Add(new DocRow());

        var document = new DocDocument
        {
            Title = "Balance Sheet",
            FileName = "balance_sheet",
            Blocks = new List<DocBlock>
            {
                new()
                {
                    Type = DocBlockType.Variable,
                    Text = new RichText { PlainText = "@tax_rate = 0.23" }
                },
                new()
                {
                    Type = DocBlockType.Variable,
                    Text = new RichText { PlainText = "@gross_margin = 0.41" }
                }
            }
        };

        var workspace = CreateIsolatedWorkspace();
        workspace.Project = new DocProject
        {
            Name = "DocumentCompletionProject",
            Tables = new List<DocTable> { table },
            Documents = new List<DocDocument> { document }
        };
        workspace.ActiveTable = table;
        return workspace;
    }
}
