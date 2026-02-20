using System.IO;
using System.Linq;
using System.Globalization;
using Derp.Doc.Commands;
using Derp.Doc.Editor;
using Derp.Doc.Model;
using Derp.Doc.Storage;
using Derp.Doc.Tables;

namespace Derp.Doc.Tests;

public sealed class Phase4DerivedTableTests
{
    [Fact]
    public void DerivedTables_RoundTripProjectedColumnsAndStepIds()
    {
        string dir = Path.Combine(Path.GetTempPath(), "derpdoc_phase4_" + Guid.NewGuid().ToString("N"));
        try
        {
            var items = new DocTable { Name = "Items", FileName = "items" };
            var itemKey = new DocColumn { Name = "Key", Kind = DocColumnKind.Text };
            items.Columns.Add(itemKey);
            var rowA = new DocRow { Id = "a" };
            rowA.SetCell(itemKey.Id, DocCellValue.Text("A"));
            items.Rows.Add(rowA);

            var stats = new DocTable { Name = "Stats", FileName = "stats" };
            var statKey = new DocColumn { Name = "Key", Kind = DocColumnKind.Text };
            var statValue = new DocColumn { Name = "Value", Kind = DocColumnKind.Number };
            stats.Columns.Add(statKey);
            stats.Columns.Add(statValue);
            var statRow = new DocRow { Id = "s1" };
            statRow.SetCell(statKey.Id, DocCellValue.Text("A"));
            statRow.SetCell(statValue.Id, DocCellValue.Number(10));
            stats.Rows.Add(statRow);

            var derived = new DocTable { Name = "Derived", FileName = "derived" };
            var outKeyCol = new DocColumn { Id = "out_key", Name = "Key", Kind = DocColumnKind.Text, IsProjected = true };
            var outValCol = new DocColumn { Id = "out_val", Name = "Value", Kind = DocColumnKind.Number, IsProjected = true };
            derived.Columns.Add(outKeyCol);
            derived.Columns.Add(outValCol);

            var joinStep = new DerivedStep
            {
                Kind = DerivedStepKind.Join,
                SourceTableId = stats.Id,
                JoinKind = DerivedJoinKind.Left,
                KeyMappings =
                {
                    new DerivedKeyMapping { BaseColumnId = outKeyCol.Id, SourceColumnId = statKey.Id }
                }
            };

            derived.DerivedConfig = new DocDerivedConfig
            {
                BaseTableId = items.Id,
                Steps = { joinStep },
                Projections =
                {
                    new DerivedProjection { SourceTableId = items.Id, SourceColumnId = itemKey.Id, OutputColumnId = outKeyCol.Id },
                    new DerivedProjection { SourceTableId = stats.Id, SourceColumnId = statValue.Id, OutputColumnId = outValCol.Id },
                }
            };

            var project = new DocProject { Name = "Phase4RoundTrip" };
            project.Tables.Add(items);
            project.Tables.Add(stats);
            project.Tables.Add(derived);

            // Evaluate once to ensure we materialize without exceptions.
            var engine = new DocFormulaEngine();
            engine.EvaluateProject(project);

            ProjectSerializer.Save(project, dir);

            var loaded = ProjectLoader.Load(dir);
            var loadedDerived = loaded.Tables.Single(t => t.Id == derived.Id);

            Assert.True(loadedDerived.IsDerived);
            Assert.Contains(loadedDerived.Columns, c => c.Id == outKeyCol.Id && c.IsProjected);
            Assert.Contains(loadedDerived.Columns, c => c.Id == outValCol.Id && c.IsProjected);
            Assert.NotEmpty(loadedDerived.DerivedConfig!.Steps[0].Id);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void DerivedTables_MixedPipeline_AppendAndJoinMaterializesDeterministically()
    {
        var items = new DocTable { Name = "Items", FileName = "items" };
        var itemKey = new DocColumn { Name = "Key", Kind = DocColumnKind.Text };
        items.Columns.Add(itemKey);
        items.Rows.Add(MakeRow("a", itemKey, "A"));
        items.Rows.Add(MakeRow("b", itemKey, "B"));

        var stats = new DocTable { Name = "Stats", FileName = "stats" };
        var statKey = new DocColumn { Name = "Key", Kind = DocColumnKind.Text };
        var statValue = new DocColumn { Name = "Value", Kind = DocColumnKind.Number };
        stats.Columns.Add(statKey);
        stats.Columns.Add(statValue);
        stats.Rows.Add(MakeRow("s1", statKey, "A", statValue, 1));
        stats.Rows.Add(MakeRow("s2", statKey, "B", statValue, 2));

        var derived = new DocTable { Name = "InventoryView", FileName = "inventoryview" };
        var outKeyCol = new DocColumn { Id = "out_key", Name = "Key", Kind = DocColumnKind.Text, IsProjected = true };
        var outValCol = new DocColumn { Id = "out_val", Name = "Value", Kind = DocColumnKind.Number, IsProjected = true };
        derived.Columns.Add(outKeyCol);
        derived.Columns.Add(outValCol);

        var appendStep = new DerivedStep { Kind = DerivedStepKind.Append, SourceTableId = items.Id };
        var joinStep = new DerivedStep
        {
            Kind = DerivedStepKind.Join,
            SourceTableId = stats.Id,
            JoinKind = DerivedJoinKind.Left,
            KeyMappings = { new DerivedKeyMapping { BaseColumnId = outKeyCol.Id, SourceColumnId = statKey.Id } }
        };

        derived.DerivedConfig = new DocDerivedConfig
        {
            BaseTableId = items.Id,
            Steps = { appendStep, joinStep },
            Projections =
            {
                new DerivedProjection { SourceTableId = items.Id, SourceColumnId = itemKey.Id, OutputColumnId = outKeyCol.Id },
                new DerivedProjection { SourceTableId = stats.Id, SourceColumnId = statValue.Id, OutputColumnId = outValCol.Id },
            }
        };

        var project = new DocProject { Name = "Phase4Mixed" };
        project.Tables.Add(items);
        project.Tables.Add(stats);
        project.Tables.Add(derived);

        var engine = new DocFormulaEngine();
        engine.EvaluateProject(project);

        // Base seed (2) + append (2) = 4 rows
        Assert.Equal(4, derived.Rows.Count);
        Assert.All(derived.Rows, r =>
        {
            string key = r.Cells[outKeyCol.Id].StringValue ?? "";
            double val = r.Cells[outValCol.Id].NumberValue;
            if (key == "A") Assert.Equal(1, val);
            else if (key == "B") Assert.Equal(2, val);
            else Assert.True(false, "unexpected key");
        });
    }

    [Fact]
    public void DerivedTables_JoinStepWithUnsetKeyMapping_IsAutoInitialized()
    {
        var tasks = new DocTable { Name = "Tasks", FileName = "tasks" };
        var taskName = new DocColumn { Name = "Name", Kind = DocColumnKind.Text };
        tasks.Columns.Add(taskName);
        tasks.Rows.Add(MakeRow("t1", taskName, "Alice"));

        var people = new DocTable { Name = "People", FileName = "people" };
        var peopleName = new DocColumn { Name = "Name", Kind = DocColumnKind.Text };
        var active = new DocColumn { Name = "Active", Kind = DocColumnKind.Checkbox };
        people.Columns.Add(peopleName);
        people.Columns.Add(active);
        var person = new DocRow { Id = "p1" };
        person.SetCell(peopleName.Id, DocCellValue.Text("Alice"));
        person.SetCell(active.Id, DocCellValue.Bool(true));
        people.Rows.Add(person);

        var derived = new DocTable { Name = "Derived", FileName = "derived" };
        derived.DerivedConfig = new DocDerivedConfig
        {
            BaseTableId = tasks.Id,
            Steps =
            {
                new DerivedStep
                {
                    Kind = DerivedStepKind.Join,
                    SourceTableId = people.Id,
                    JoinKind = DerivedJoinKind.Left,
                    KeyMappings =
                    {
                        // Simulates the UI showing a default dropdown selection, but the stored ids were never written.
                        new DerivedKeyMapping { BaseColumnId = "", SourceColumnId = "" }
                    }
                }
            }
        };

        var project = new DocProject { Name = "Phase4AutoInit" };
        project.Tables.Add(tasks);
        project.Tables.Add(people);
        project.Tables.Add(derived);

        var engine = new DocFormulaEngine();
        engine.EvaluateProject(project);

        var step = derived.DerivedConfig!.Steps[0];
        Assert.NotEmpty(step.KeyMappings);
        Assert.False(string.IsNullOrEmpty(step.KeyMappings[0].BaseColumnId));
        Assert.False(string.IsNullOrEmpty(step.KeyMappings[0].SourceColumnId));

        // Ensure we actually joined by Name and projected Active from People.
        string? activeOutId = null;
        for (int i = 0; i < derived.DerivedConfig!.Projections.Count; i++)
        {
            var proj = derived.DerivedConfig!.Projections[i];
            if (proj.SourceTableId == people.Id && proj.SourceColumnId == active.Id)
            {
                activeOutId = proj.OutputColumnId;
                break;
            }
        }
        Assert.False(string.IsNullOrEmpty(activeOutId));

        Assert.Single(derived.Rows);

        DocColumn? activeOutCol = null;
        for (int i = 0; i < derived.Columns.Count; i++)
        {
            var col = derived.Columns[i];
            if (col.Id == activeOutId)
            {
                activeOutCol = col;
                break;
            }
        }
        Assert.NotNull(activeOutCol);
        Assert.True(derived.Rows[0].GetCell(activeOutCol!).BoolValue);
    }

    [Fact]
    public void DerivedTables_MoveColumn_ReordersProjectionOrderAndUndoRedo()
    {
        var baseTable = new DocTable { Name = "Base", FileName = "base" };
        var nameColumn = new DocColumn { Name = "Name", Kind = DocColumnKind.Text };
        var scoreColumn = new DocColumn { Name = "Score", Kind = DocColumnKind.Number };
        baseTable.Columns.Add(nameColumn);
        baseTable.Columns.Add(scoreColumn);
        baseTable.Rows.Add(MakeRow("r1", nameColumn, "Alpha", scoreColumn, 10));

        var derived = new DocTable { Name = "Derived", FileName = "derived" };
        var outName = new DocColumn { Id = "out_name", Name = "Name", Kind = DocColumnKind.Text, IsProjected = true };
        var outScore = new DocColumn { Id = "out_score", Name = "Score", Kind = DocColumnKind.Number, IsProjected = true };
        derived.Columns.Add(outName);
        derived.Columns.Add(outScore);
        derived.DerivedConfig = new DocDerivedConfig
        {
            BaseTableId = baseTable.Id,
            Projections =
            {
                new DerivedProjection { SourceTableId = baseTable.Id, SourceColumnId = nameColumn.Id, OutputColumnId = outName.Id },
                new DerivedProjection { SourceTableId = baseTable.Id, SourceColumnId = scoreColumn.Id, OutputColumnId = outScore.Id },
            }
        };

        var workspace = CreateIsolatedWorkspace();
        workspace.Project = new DocProject { Name = "DerivedMoveColumn", Tables = new List<DocTable> { baseTable, derived } };
        workspace.ActiveTable = derived;

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.MoveColumn,
            TableId = derived.Id,
            ColumnIndex = 1,
            TargetColumnIndex = 0,
        });

        Assert.Equal(outScore.Id, derived.DerivedConfig!.Projections[0].OutputColumnId);
        Assert.Equal(outName.Id, derived.DerivedConfig!.Projections[1].OutputColumnId);
        Assert.Equal("Score", derived.Columns[0].Name);
        Assert.Equal("Name", derived.Columns[1].Name);

        workspace.Undo();
        Assert.Equal(outName.Id, derived.DerivedConfig!.Projections[0].OutputColumnId);
        Assert.Equal(outScore.Id, derived.DerivedConfig!.Projections[1].OutputColumnId);
        Assert.Equal("Name", derived.Columns[0].Name);
        Assert.Equal("Score", derived.Columns[1].Name);

        workspace.Redo();
        Assert.Equal(outScore.Id, derived.DerivedConfig!.Projections[0].OutputColumnId);
        Assert.Equal(outName.Id, derived.DerivedConfig!.Projections[1].OutputColumnId);
    }

    [Fact]
    public void DerivedTables_SetDerivedBaseTableCommand_AutoProjectsAndMaterializesRows()
    {
        var baseTable = new DocTable { Name = "Base", FileName = "base" };
        var nameColumn = new DocColumn { Name = "Name", Kind = DocColumnKind.Text };
        var scoreColumn = new DocColumn { Name = "Score", Kind = DocColumnKind.Number };
        baseTable.Columns.Add(nameColumn);
        baseTable.Columns.Add(scoreColumn);
        baseTable.Rows.Add(MakeRow("r1", nameColumn, "Alpha", scoreColumn, 10));
        baseTable.Rows.Add(MakeRow("r2", nameColumn, "Beta", scoreColumn, 20));

        var derived = new DocTable { Name = "Derived", FileName = "derived", DerivedConfig = new DocDerivedConfig() };

        var workspace = CreateIsolatedWorkspace();
        workspace.Project = new DocProject { Name = "DerivedSetBase", Tables = new List<DocTable> { baseTable, derived } };
        workspace.SetActiveTable(derived);

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetDerivedBaseTable,
            TableId = derived.Id,
            OldBaseTableId = null,
            NewBaseTableId = baseTable.Id,
        });

        Assert.Equal(baseTable.Id, derived.DerivedConfig!.BaseTableId);
        Assert.Equal(baseTable.Columns.Count, derived.DerivedConfig.Projections.Count);
        Assert.Equal(baseTable.Rows.Count, derived.Rows.Count);

        var nameProjection = derived.DerivedConfig.Projections.Single(p =>
            string.Equals(p.SourceTableId, baseTable.Id, StringComparison.Ordinal) &&
            string.Equals(p.SourceColumnId, nameColumn.Id, StringComparison.Ordinal));
        var scoreProjection = derived.DerivedConfig.Projections.Single(p =>
            string.Equals(p.SourceTableId, baseTable.Id, StringComparison.Ordinal) &&
            string.Equals(p.SourceColumnId, scoreColumn.Id, StringComparison.Ordinal));

        var derivedNameColumn = derived.Columns.Single(c => string.Equals(c.Id, nameProjection.OutputColumnId, StringComparison.Ordinal));
        var derivedScoreColumn = derived.Columns.Single(c => string.Equals(c.Id, scoreProjection.OutputColumnId, StringComparison.Ordinal));

        Assert.Equal("Alpha", derived.Rows[0].GetCell(derivedNameColumn).StringValue);
        Assert.Equal(10, derived.Rows[0].GetCell(derivedScoreColumn).NumberValue);
    }

    [Fact]
    public void DerivedTables_StrictMultiMatch_ProducesMultiMatchDiagnosticsAndDoesNotPick()
    {
        var items = new DocTable { Name = "Items", FileName = "items" };
        var itemKey = new DocColumn { Name = "Key", Kind = DocColumnKind.Text };
        items.Columns.Add(itemKey);
        items.Rows.Add(MakeRow("a", itemKey, "A"));

        var stats = new DocTable { Name = "Stats", FileName = "stats" };
        var statKey = new DocColumn { Name = "Key", Kind = DocColumnKind.Text };
        var statValue = new DocColumn { Name = "Value", Kind = DocColumnKind.Number };
        stats.Columns.Add(statKey);
        stats.Columns.Add(statValue);
        stats.Rows.Add(MakeRow("s1", statKey, "A", statValue, 1));
        stats.Rows.Add(MakeRow("s2", statKey, "A", statValue, 2));

        var derived = new DocTable { Name = "View", FileName = "view" };
        var outKeyCol = new DocColumn { Id = "out_key", Name = "Key", Kind = DocColumnKind.Text, IsProjected = true };
        var outValCol = new DocColumn { Id = "out_val", Name = "Value", Kind = DocColumnKind.Number, IsProjected = true };
        derived.Columns.Add(outKeyCol);
        derived.Columns.Add(outValCol);

        derived.DerivedConfig = new DocDerivedConfig
        {
            BaseTableId = items.Id,
            Steps =
            {
                new DerivedStep
                {
                    Kind = DerivedStepKind.Join,
                    SourceTableId = stats.Id,
                    JoinKind = DerivedJoinKind.Left,
                    KeyMappings = { new DerivedKeyMapping { BaseColumnId = outKeyCol.Id, SourceColumnId = statKey.Id } }
                }
            },
            Projections =
            {
                new DerivedProjection { SourceTableId = items.Id, SourceColumnId = itemKey.Id, OutputColumnId = outKeyCol.Id },
                new DerivedProjection { SourceTableId = stats.Id, SourceColumnId = statValue.Id, OutputColumnId = outValCol.Id },
            }
        };

        var project = new DocProject { Name = "Phase4MultiMatch" };
        project.Tables.Add(items);
        project.Tables.Add(stats);
        project.Tables.Add(derived);

        var engine = new DocFormulaEngine();
        engine.EvaluateProject(project);

        var res = engine.DerivedResults[derived.Id];
        Assert.Equal(1, res.MultiMatchCount);
        Assert.Equal(0, derived.Rows[0].GetCell(outValCol).NumberValue);
    }

    [Fact]
    public void DerivedTables_NumericKeyMatching_IsCultureInvariant()
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            var items = new DocTable { Name = "Items", FileName = "items" };
            var itemKey = new DocColumn { Name = "Key", Kind = DocColumnKind.Number };
            items.Columns.Add(itemKey);
            var row = new DocRow { Id = "a" };
            row.SetCell(itemKey.Id, DocCellValue.Number(1.5));
            items.Rows.Add(row);

            var stats = new DocTable { Name = "Stats", FileName = "stats" };
            var statKey = new DocColumn { Name = "Key", Kind = DocColumnKind.Number };
            var statValue = new DocColumn { Name = "Value", Kind = DocColumnKind.Number };
            stats.Columns.Add(statKey);
            stats.Columns.Add(statValue);
            var srow = new DocRow { Id = "s1" };
            srow.SetCell(statKey.Id, DocCellValue.Number(1.5));
            srow.SetCell(statValue.Id, DocCellValue.Number(42));
            stats.Rows.Add(srow);

            var derived = new DocTable { Name = "View", FileName = "view" };
            var outKeyCol = new DocColumn { Id = "out_key", Name = "Key", Kind = DocColumnKind.Number, IsProjected = true };
            var outValCol = new DocColumn { Id = "out_val", Name = "Value", Kind = DocColumnKind.Number, IsProjected = true };
            derived.Columns.Add(outKeyCol);
            derived.Columns.Add(outValCol);

            derived.DerivedConfig = new DocDerivedConfig
            {
                BaseTableId = items.Id,
                Steps =
                {
                    new DerivedStep
                    {
                        Kind = DerivedStepKind.Join,
                        SourceTableId = stats.Id,
                        JoinKind = DerivedJoinKind.Left,
                        KeyMappings = { new DerivedKeyMapping { BaseColumnId = outKeyCol.Id, SourceColumnId = statKey.Id } }
                    }
                },
                Projections =
                {
                    new DerivedProjection { SourceTableId = items.Id, SourceColumnId = itemKey.Id, OutputColumnId = outKeyCol.Id },
                    new DerivedProjection { SourceTableId = stats.Id, SourceColumnId = statValue.Id, OutputColumnId = outValCol.Id },
                }
            };

            var project = new DocProject { Name = "Phase4Culture" };
            project.Tables.Add(items);
            project.Tables.Add(stats);
            project.Tables.Add(derived);

            var engine = new DocFormulaEngine();
            engine.EvaluateProject(project);

            Assert.Equal(42, derived.Rows[0].GetCell(outValCol).NumberValue);
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }

    [Fact]
    public void DerivedTables_OwnDataPersistsAgainstStableOutRowKeyIds()
    {
        string dir = Path.Combine(Path.GetTempPath(), "derpdoc_phase4_owndata_" + Guid.NewGuid().ToString("N"));
        try
        {
            var items = new DocTable { Name = "Items", FileName = "items" };
            var itemKey = new DocColumn { Name = "Key", Kind = DocColumnKind.Text };
            items.Columns.Add(itemKey);
            items.Rows.Add(MakeRow("a", itemKey, "A"));
            items.Rows.Add(MakeRow("b", itemKey, "B"));

            var derived = new DocTable { Name = "View", FileName = "view" };
            var outKeyCol = new DocColumn { Id = "out_key", Name = "Key", Kind = DocColumnKind.Text, IsProjected = true };
            var noteCol = new DocColumn { Id = "note", Name = "Note", Kind = DocColumnKind.Text, IsProjected = false };
            derived.Columns.Add(outKeyCol);
            derived.Columns.Add(noteCol);

            derived.DerivedConfig = new DocDerivedConfig
            {
                BaseTableId = items.Id,
                Steps = { },
                Projections =
                {
                    new DerivedProjection { SourceTableId = items.Id, SourceColumnId = itemKey.Id, OutputColumnId = outKeyCol.Id },
                }
            };

            var project = new DocProject { Name = "Phase4OwnData" };
            project.Tables.Add(items);
            project.Tables.Add(derived);

            var engine = new DocFormulaEngine();
            engine.EvaluateProject(project);

            // Add local value for key "A"
            var aRow = derived.Rows.Single(r => r.GetCell(outKeyCol).StringValue == "A");
            aRow.SetCell(noteCol.Id, DocCellValue.Text("hello"));

            ProjectSerializer.Save(project, dir);

            var loaded = ProjectLoader.Load(dir);
            var loadedDerived = loaded.Tables.Single(t => t.Id == derived.Id);

            // Re-evaluate to materialize + restore own-data
            var engine2 = new DocFormulaEngine();
            engine2.EvaluateProject(loaded);

            var aRow2 = loadedDerived.Rows.Single(r => r.GetCell(outKeyCol).StringValue == "A");
            Assert.Equal("hello", aRow2.GetCell(noteCol).StringValue);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void DerivedTables_RemoveStep_PrunesInactiveSourceProjections()
    {
        var items = new DocTable { Name = "Items", FileName = "items" };
        var itemKey = new DocColumn { Name = "Key", Kind = DocColumnKind.Text };
        items.Columns.Add(itemKey);
        items.Rows.Add(MakeRow("a", itemKey, "A"));

        var stats = new DocTable { Name = "Stats", FileName = "stats" };
        var statKey = new DocColumn { Name = "Key", Kind = DocColumnKind.Text };
        var statValue = new DocColumn { Name = "Value", Kind = DocColumnKind.Number };
        stats.Columns.Add(statKey);
        stats.Columns.Add(statValue);
        stats.Rows.Add(MakeRow("s1", statKey, "A", statValue, 10));

        var derived = new DocTable { Name = "View", FileName = "view" };
        var outKey = new DocColumn { Id = "out_key", Name = "Key", Kind = DocColumnKind.Text, IsProjected = true };
        var outValue = new DocColumn { Id = "out_value", Name = "Value", Kind = DocColumnKind.Number, IsProjected = true };
        derived.Columns.Add(outKey);
        derived.Columns.Add(outValue);
        derived.DerivedConfig = new DocDerivedConfig
        {
            BaseTableId = items.Id,
            Steps =
            {
                new DerivedStep
                {
                    Kind = DerivedStepKind.Join,
                    SourceTableId = stats.Id,
                    JoinKind = DerivedJoinKind.Left,
                    KeyMappings = { new DerivedKeyMapping { BaseColumnId = outKey.Id, SourceColumnId = statKey.Id } }
                }
            },
            Projections =
            {
                new DerivedProjection { SourceTableId = items.Id, SourceColumnId = itemKey.Id, OutputColumnId = outKey.Id },
                new DerivedProjection { SourceTableId = stats.Id, SourceColumnId = statValue.Id, OutputColumnId = outValue.Id },
            }
        };

        var project = new DocProject { Name = "Phase4RemoveStep" };
        project.Tables.Add(items);
        project.Tables.Add(stats);
        project.Tables.Add(derived);

        var engine = new DocFormulaEngine();
        engine.EvaluateProject(project);
        Assert.Equal(10, derived.Rows.Single().GetCell(outValue).NumberValue);

        derived.DerivedConfig.Steps.Clear();
        engine.EvaluateProject(project);

        Assert.DoesNotContain(
            derived.DerivedConfig.Projections,
            projection => string.Equals(projection.SourceTableId, stats.Id, StringComparison.Ordinal));
        Assert.DoesNotContain(derived.Columns, column => string.Equals(column.Id, outValue.Id, StringComparison.Ordinal));
        Assert.Single(derived.Rows);
        Assert.Equal("A", derived.Rows.Single().GetCell(outKey).StringValue);
    }

    [Fact]
    public void DerivedTables_BaseSubtable_InheritsParentBinding()
    {
        var parentTable = new DocTable { Name = "Characters", FileName = "characters" };
        var parentName = new DocColumn { Name = "Name", Kind = DocColumnKind.Text };
        parentTable.Columns.Add(parentName);

        var parentRowA = new DocRow { Id = "char_a" };
        parentRowA.SetCell(parentName.Id, DocCellValue.Text("Alice"));
        parentTable.Rows.Add(parentRowA);

        var parentRowB = new DocRow { Id = "char_b" };
        parentRowB.SetCell(parentName.Id, DocCellValue.Text("Bob"));
        parentTable.Rows.Add(parentRowB);

        var childTable = new DocTable
        {
            Name = "Characters_Items",
            FileName = "characters_items",
            ParentTableId = parentTable.Id,
        };
        var parentRowIdColumn = new DocColumn { Name = "_parentRowId", Kind = DocColumnKind.Text, IsHidden = true };
        var itemName = new DocColumn { Name = "Item", Kind = DocColumnKind.Text };
        childTable.ParentRowColumnId = parentRowIdColumn.Id;
        childTable.Columns.Add(parentRowIdColumn);
        childTable.Columns.Add(itemName);

        var childRowA = new DocRow { Id = "item_a" };
        childRowA.SetCell(parentRowIdColumn.Id, DocCellValue.Text(parentRowA.Id));
        childRowA.SetCell(itemName.Id, DocCellValue.Text("Sword"));
        childTable.Rows.Add(childRowA);

        var childRowB = new DocRow { Id = "item_b" };
        childRowB.SetCell(parentRowIdColumn.Id, DocCellValue.Text(parentRowB.Id));
        childRowB.SetCell(itemName.Id, DocCellValue.Text("Shield"));
        childTable.Rows.Add(childRowB);

        var derived = new DocTable
        {
            Name = "Characters_Items_View",
            FileName = "characters_items_view",
            DerivedConfig = new DocDerivedConfig
            {
                BaseTableId = childTable.Id,
            }
        };

        var project = new DocProject { Name = "Phase4SubtableDerived" };
        project.Tables.Add(parentTable);
        project.Tables.Add(childTable);
        project.Tables.Add(derived);

        var engine = new DocFormulaEngine();
        engine.EvaluateProject(project);

        Assert.Equal(parentTable.Id, derived.ParentTableId);
        Assert.False(string.IsNullOrEmpty(derived.ParentRowColumnId));

        var parentRowProjection = derived.DerivedConfig!.Projections.Single(projection =>
            string.Equals(projection.SourceTableId, childTable.Id, StringComparison.Ordinal) &&
            string.Equals(projection.SourceColumnId, parentRowIdColumn.Id, StringComparison.Ordinal));
        Assert.Equal(parentRowProjection.OutputColumnId, derived.ParentRowColumnId);

        var derivedParentRowColumn = derived.Columns.Single(column =>
            string.Equals(column.Id, derived.ParentRowColumnId, StringComparison.Ordinal));
        Assert.True(derivedParentRowColumn.IsHidden);

        var derivedParentIds = derived.Rows
            .Select(row => row.GetCell(derivedParentRowColumn).StringValue ?? "")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { parentRowA.Id, parentRowB.Id }, derivedParentIds);
    }

    [Fact]
    public void DerivedTables_AppendWithSubtable_ProjectsSubtableColumnMetadata()
    {
        var inventory = new DocTable { Name = "Inventory", FileName = "inventory" };
        var ownerName = new DocColumn { Name = "Owner", Kind = DocColumnKind.Text };
        var itemsColumn = new DocColumn { Name = "Items", Kind = DocColumnKind.Subtable };
        inventory.Columns.Add(ownerName);
        inventory.Columns.Add(itemsColumn);

        var inventoryRowA = new DocRow { Id = "inv_a" };
        inventoryRowA.SetCell(ownerName.Id, DocCellValue.Text("Alice"));
        inventory.Rows.Add(inventoryRowA);

        var inventoryRowB = new DocRow { Id = "inv_b" };
        inventoryRowB.SetCell(ownerName.Id, DocCellValue.Text("Bob"));
        inventory.Rows.Add(inventoryRowB);

        var inventoryItems = new DocTable
        {
            Name = "Inventory_Items",
            FileName = "inventory_items",
            ParentTableId = inventory.Id,
        };
        var parentRowIdColumn = new DocColumn { Name = "_parentRowId", Kind = DocColumnKind.Text, IsHidden = true };
        var itemNameColumn = new DocColumn { Name = "Item", Kind = DocColumnKind.Text };
        inventoryItems.ParentRowColumnId = parentRowIdColumn.Id;
        inventoryItems.Columns.Add(parentRowIdColumn);
        inventoryItems.Columns.Add(itemNameColumn);
        itemsColumn.SubtableId = inventoryItems.Id;

        var childA1 = new DocRow { Id = "child_a1" };
        childA1.SetCell(parentRowIdColumn.Id, DocCellValue.Text(inventoryRowA.Id));
        childA1.SetCell(itemNameColumn.Id, DocCellValue.Text("Sword"));
        inventoryItems.Rows.Add(childA1);

        var childA2 = new DocRow { Id = "child_a2" };
        childA2.SetCell(parentRowIdColumn.Id, DocCellValue.Text(inventoryRowA.Id));
        childA2.SetCell(itemNameColumn.Id, DocCellValue.Text("Potion"));
        inventoryItems.Rows.Add(childA2);

        var childB1 = new DocRow { Id = "child_b1" };
        childB1.SetCell(parentRowIdColumn.Id, DocCellValue.Text(inventoryRowB.Id));
        childB1.SetCell(itemNameColumn.Id, DocCellValue.Text("Shield"));
        inventoryItems.Rows.Add(childB1);

        var appendStep = new DerivedStep
        {
            Id = "append_inventory",
            Kind = DerivedStepKind.Append,
            SourceTableId = inventory.Id,
        };

        var derived = new DocTable
        {
            Name = "InventoryView",
            FileName = "inventory_view",
            DerivedConfig = new DocDerivedConfig
            {
                Steps = { appendStep },
            }
        };

        var project = new DocProject { Name = "Phase4AppendSubtable" };
        project.Tables.Add(inventory);
        project.Tables.Add(inventoryItems);
        project.Tables.Add(derived);

        var engine = new DocFormulaEngine();
        engine.EvaluateProject(project);

        var projectedItemsColumn = derived.Columns.Single(column =>
            column.Kind == DocColumnKind.Subtable &&
            string.Equals(column.Name, itemsColumn.Name, StringComparison.Ordinal));
        Assert.Equal(inventoryItems.Id, projectedItemsColumn.SubtableId);

        var rowKeys = engine.DerivedResults[derived.Id].RowKeys;
        Assert.Equal(2, rowKeys.Count);
        Assert.Equal(appendStep.Id, rowKeys[0].TableId);
        Assert.Equal(inventoryRowA.Id, rowKeys[0].RowId);
        Assert.Equal(appendStep.Id, rowKeys[1].TableId);
        Assert.Equal(inventoryRowB.Id, rowKeys[1].RowId);
    }

    private static DocRow MakeRow(string id, DocColumn a, string aVal)
    {
        var r = new DocRow { Id = id };
        r.SetCell(a.Id, DocCellValue.Text(aVal));
        return r;
    }

    private static DocRow MakeRow(string id, DocColumn a, string aVal, DocColumn b, double bVal)
    {
        var r = new DocRow { Id = id };
        r.SetCell(a.Id, DocCellValue.Text(aVal));
        r.SetCell(b.Id, DocCellValue.Number(bVal));
        return r;
    }

    private static DocWorkspace CreateIsolatedWorkspace()
    {
        var workspace = new DocWorkspace();
        workspace.AutoSave = false;
        workspace.ProjectPath = null;
        return workspace;
    }
}
