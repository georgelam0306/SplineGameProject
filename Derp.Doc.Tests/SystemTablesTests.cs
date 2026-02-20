using Derp.Doc.Model;
using Derp.Doc.Storage;
using Derp.Doc.Tables;

namespace Derp.Doc.Tests;

public sealed class SystemTablesTests
{
    [Fact]
    public void ProjectLoader_ReconcilesSystemTables_AndScansAssets()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_system_tables_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            string gameRoot = DocProjectScaffolder.EnsureGameRoot(root, "SystemTablesProject");
            string dbRoot = Path.Combine(gameRoot, "Database");

            WriteAsset(Path.Combine(gameRoot, "Assets", "Env", "Tree.png"), [0x01, 0x02, 0x03]);
            WriteAsset(Path.Combine(gameRoot, "Assets", "Sfx", "Hit.wav"), [0x10, 0x11, 0x12]);

            DocProject project = ProjectLoader.Load(dbRoot);

            AssertSystemTableExists(project, DocSystemTableKeys.Assets, dataLocked: true);
            AssertSystemTableExists(project, DocSystemTableKeys.Packages, dataLocked: false);
            AssertSystemTableExists(project, DocSystemTableKeys.Exports, dataLocked: false);
            AssertSystemTableExists(project, DocSystemTableKeys.Textures, dataLocked: true);
            AssertSystemTableExists(project, DocSystemTableKeys.Models, dataLocked: true);
            AssertSystemTableExists(project, DocSystemTableKeys.Audio, dataLocked: true);
            AssertSystemTableExists(project, DocSystemTableKeys.Materials, dataLocked: true);
            AssertSystemTableExists(project, DocSystemTableKeys.AssetDependencies, dataLocked: true);
            AssertSystemTableExists(project, DocSystemTableKeys.SplineGameEntityBase, dataLocked: false);
            AssertSystemTableExists(project, DocSystemTableKeys.SplineGameEntityTools, dataLocked: false);

            DocTable assetsTable = FindSystemTable(project, DocSystemTableKeys.Assets);
            Assert.Equal(2, assetsTable.Rows.Count);
            Assert.All(assetsTable.Rows, row => Assert.False(row.GetCell("missing").BoolValue));
            Assert.Contains(assetsTable.Rows, row => row.GetCell("name").StringValue == "Env_Tree_png");
            Assert.Contains(assetsTable.Rows, row => row.GetCell("name").StringValue == "Sfx_Hit_wav");
            DocColumn assetsAssetIdColumn = assetsTable.Columns.Single(column => column.Id == "asset_id");
            Assert.Equal(DocColumnKind.Number, assetsAssetIdColumn.Kind);
            Assert.Equal("int", assetsAssetIdColumn.ExportType);

            DocTable packagesTable = FindSystemTable(project, DocSystemTableKeys.Packages);
            DocColumn packagesPackageIdColumn = packagesTable.Columns.Single(column => column.Id == "package_id");
            Assert.Equal("int", packagesPackageIdColumn.ExportType);

            DocTable exportsTable = FindSystemTable(project, DocSystemTableKeys.Exports);
            DocColumn exportsPackageColumn = exportsTable.Columns.Single(column => column.Id == "package_id");
            DocColumn exportsAssetColumn = exportsTable.Columns.Single(column => column.Id == "asset_id");
            Assert.Equal(DocColumnKind.Relation, exportsPackageColumn.Kind);
            Assert.Equal(DocColumnKind.Relation, exportsAssetColumn.Kind);
            Assert.Equal(packagesTable.Id, exportsPackageColumn.RelationTableId);
            Assert.Equal(assetsTable.Id, exportsAssetColumn.RelationTableId);

            DocTable texturesTable = FindSystemTable(project, DocSystemTableKeys.Textures);
            DocTable modelsTable = FindSystemTable(project, DocSystemTableKeys.Models);
            DocTable audioTable = FindSystemTable(project, DocSystemTableKeys.Audio);
            Assert.Equal(DocColumnKind.TextureAsset, texturesTable.Columns.Single(column => column.Id == "preview").Kind);
            Assert.Equal(DocColumnKind.MeshAsset, modelsTable.Columns.Single(column => column.Id == "preview").Kind);
            Assert.Equal(DocColumnKind.AudioAsset, audioTable.Columns.Single(column => column.Id == "preview").Kind);
            Assert.Single(texturesTable.Rows);
            Assert.Single(audioTable.Rows);
            Assert.Equal("Env/Tree.png", texturesTable.Rows[0].GetCell("preview").StringValue);
            Assert.Equal("Sfx/Hit.wav", audioTable.Rows[0].GetCell("preview").StringValue);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void SystemAssets_ReattachesRenamedFile_ByContentHash_AndKeepsAssetId()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_system_rename_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            string gameRoot = DocProjectScaffolder.EnsureGameRoot(root, "SystemTablesRenameProject");
            string dbRoot = Path.Combine(gameRoot, "Database");
            string originalPath = Path.Combine(gameRoot, "Assets", "Env", "Tree.png");
            string renamedPath = Path.Combine(gameRoot, "Assets", "Env", "TreeRenamed.png");

            WriteAsset(originalPath, [0x21, 0x22, 0x23, 0x24, 0x25]);

            DocProject firstLoad = ProjectLoader.Load(dbRoot);
            DocTable firstAssetsTable = FindSystemTable(firstLoad, DocSystemTableKeys.Assets);
            Assert.Single(firstAssetsTable.Rows);
            int originalAssetId = (int)Math.Round(firstAssetsTable.Rows[0].GetCell("asset_id").NumberValue);
            Assert.True(originalAssetId > 0);
            ProjectSerializer.Save(firstLoad, dbRoot);

            Directory.CreateDirectory(Path.GetDirectoryName(renamedPath)!);
            File.Move(originalPath, renamedPath);

            DocProject secondLoad = ProjectLoader.Load(dbRoot);
            DocTable secondAssetsTable = FindSystemTable(secondLoad, DocSystemTableKeys.Assets);
            Assert.Single(secondAssetsTable.Rows);

            DocRow row = secondAssetsTable.Rows[0];
            int reattachedAssetId = (int)Math.Round(row.GetCell("asset_id").NumberValue);
            Assert.Equal(originalAssetId, reattachedAssetId);
            Assert.Equal("Env/TreeRenamed.png", row.GetCell("relative_path").StringValue);
            Assert.False(row.GetCell("missing").BoolValue);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void DerivedFilterExpression_FiltersRows_Deterministically()
    {
        var sourceTable = new DocTable { Name = "AssetsLike", FileName = "assets_like" };
        var kindColumn = new DocColumn
        {
            Id = "kind",
            Name = "Kind",
            Kind = DocColumnKind.Select,
            Options = ["Texture", "Model", "Audio"],
        };
        var nameColumn = new DocColumn
        {
            Id = "name",
            Name = "Name",
            Kind = DocColumnKind.Text,
        };
        sourceTable.Columns.Add(kindColumn);
        sourceTable.Columns.Add(nameColumn);

        sourceTable.Rows.Add(CreateAssetLikeRow(kindColumn, nameColumn, "Texture", "Tree"));
        sourceTable.Rows.Add(CreateAssetLikeRow(kindColumn, nameColumn, "Model", "Hero"));
        sourceTable.Rows.Add(CreateAssetLikeRow(kindColumn, nameColumn, "Audio", "Hit"));

        var derivedTable = new DocTable
        {
            Name = "TexturesAndAudio",
            FileName = "textures_and_audio",
            DerivedConfig = new DocDerivedConfig
            {
                BaseTableId = sourceTable.Id,
                FilterExpression = "thisRow.Kind == \"Texture\" || thisRow.Kind == \"Audio\"",
            },
        };
        derivedTable.Columns.Add(new DocColumn
        {
            Id = kindColumn.Id,
            Name = "Kind",
            Kind = DocColumnKind.Select,
            Options = ["Texture", "Model", "Audio"],
        });
        derivedTable.Columns.Add(new DocColumn
        {
            Id = nameColumn.Id,
            Name = "Name",
            Kind = DocColumnKind.Text,
        });
        derivedTable.DerivedConfig.Projections.Add(new DerivedProjection
        {
            SourceTableId = sourceTable.Id,
            SourceColumnId = kindColumn.Id,
            OutputColumnId = kindColumn.Id,
        });
        derivedTable.DerivedConfig.Projections.Add(new DerivedProjection
        {
            SourceTableId = sourceTable.Id,
            SourceColumnId = nameColumn.Id,
            OutputColumnId = nameColumn.Id,
        });

        var project = new DocProject { Name = "DerivedFilterProject" };
        project.Tables.Add(sourceTable);
        project.Tables.Add(derivedTable);

        var formulaEngine = new DocFormulaEngine();
        formulaEngine.EvaluateProject(project);

        Assert.Equal(2, derivedTable.Rows.Count);
        Assert.Contains(derivedTable.Rows, row => row.GetCell("name").StringValue == "Tree");
        Assert.Contains(derivedTable.Rows, row => row.GetCell("name").StringValue == "Hit");
        Assert.DoesNotContain(derivedTable.Rows, row => row.GetCell("name").StringValue == "Hero");
    }

    private static void AssertSystemTableExists(DocProject project, string systemKey, bool dataLocked)
    {
        DocTable table = FindSystemTable(project, systemKey);
        Assert.True(table.IsSystemTable);
        Assert.True(table.IsSystemSchemaLocked);
        Assert.Equal(dataLocked, table.IsSystemDataLocked);
    }

    private static DocTable FindSystemTable(DocProject project, string systemKey)
    {
        return project.Tables.Single(table => string.Equals(table.SystemKey, systemKey, StringComparison.Ordinal));
    }

    private static DocRow CreateAssetLikeRow(
        DocColumn kindColumn,
        DocColumn nameColumn,
        string kind,
        string name)
    {
        var row = new DocRow();
        row.SetCell(kindColumn.Id, DocCellValue.Text(kind));
        row.SetCell(nameColumn.Id, DocCellValue.Text(name));
        return row;
    }

    private static void WriteAsset(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static void TryDeleteDirectory(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
