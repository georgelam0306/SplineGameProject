using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Derp.Doc.Export;
using Derp.Doc.Model;
using Derp.Doc.Plugins;
using Derp.Doc.Tables;
using DerpDoc.Runtime;
using Microsoft.CodeAnalysis;

namespace Derp.Doc.Tests;

public sealed class Phase5ExportTests
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct ExportTestUnitData
    {
        public int Id;
        public Core.StringHandle Name;
        public int Code;
        public byte Faction;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct ExportTestNameRow
    {
        public int Id;
        public Core.StringHandle Name;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct ExportTestFormulaRow
    {
        public int Id;
        public int Value;
        public int PlusOne;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct ExportTestAssetRow
    {
        public int Id;
        public Core.StringHandle Texture;
        public Core.StringHandle Mesh;
        public Core.StringHandle Voice;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct ExportTestUuidAuthorRow
    {
        public Core.StringHandle AuthorUuid;
        public Core.StringHandle Name;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct ExportTestUuidBookRow
    {
        public int Id;
        public Core.StringHandle Title;
        public int Author;
    }

    [Fact]
    public void Binary_Header_MagicAndVersion()
    {
        var project = BuildUnitsProject(out _);
        var (result, binPath, generatedDir) = ExportToTemp(project);

        try
        {
            Assert.False(result.HasErrors);

            var bytes = File.ReadAllBytes(binPath);
            Assert.True(bytes.Length >= 24);

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4));
            uint version = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4));
            Assert.Equal(0x42444447u, magic); // "GDDB"
            Assert.Equal(2u, version);
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Binary_Checksum_DetectsCorruption()
    {
        var project = BuildUnitsProject(out _);
        var (result, binPath, generatedDir) = ExportToTemp(project);

        try
        {
            Assert.False(result.HasErrors);

            var corruptedPath = Path.Combine(Path.GetDirectoryName(binPath)!, "Corrupt.derpdoc");
            var bytes = File.ReadAllBytes(binPath);
            bytes[100] ^= 0xFF;
            File.WriteAllBytes(corruptedPath, bytes);

            var ex = Assert.ThrowsAny<Exception>(() =>
            {
                using var loader = BinaryLoader.Load(corruptedPath);
            });

            Assert.Contains("Checksum mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Binary_Alignment_16Byte()
    {
        var project = BuildUnitsProject(out _);
        var (result, binPath, generatedDir) = ExportToTemp(project);

        try
        {
            Assert.False(result.HasErrors);

            var bytes = File.ReadAllBytes(binPath);
            uint tableCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4));
            Assert.True(tableCount > 0);

            int headerSize = 24;
            int dirEntrySize = 24;
            int dirStart = headerSize;

            for (int i = 0; i < tableCount; i++)
            {
                int off = dirStart + i * dirEntrySize;
                uint recordOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(off + 4, 4));
                uint slotOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(off + 16, 4));

                Assert.Equal(0u, recordOffset % 16u);
                Assert.Equal(0u, slotOffset % 16u);
            }

            uint stringRegistryOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16, 4));
            if (stringRegistryOffset != 0)
            {
                Assert.Equal(0u, stringRegistryOffset % 16u);
            }
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Export_PrimaryKey_Duplicate_IsHardError()
    {
        var project = BuildUnitsProject(out var units);
        units.Rows.Add(CloneRowWithId(units.Rows[0], "r_dup"));
        units.Rows[^1].SetCell(units.Keys.PrimaryKeyColumnId, DocCellValue.Number(0));

        var (result, binPath, generatedDir) = ExportToTemp(project);

        try
        {
            Assert.True(result.HasErrors);
            Assert.Contains(result.Diagnostics, d => d.Code == "export/key/pk-duplicate");
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Export_SecondaryKey_Unique_EnforcedAtExport()
    {
        var project = BuildUnitsProject(out var units);
        units.Rows[1].SetCell(units.Columns[2].Id, DocCellValue.Number(100)); // Code duplicate

        var (result, binPath, generatedDir) = ExportToTemp(project);

        try
        {
            Assert.True(result.HasErrors);
            Assert.Contains(result.Diagnostics, d => d.Code == "export/key/secondary-duplicate");
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Export_PrimaryKey_IdColumn_MapsUuidToStableInt_And_RelationsUseMappedKeys()
    {
        var project = BuildUuidPrimaryKeyRelationProject(out _);
        var (result, binPath, _) = ExportToTemp(project);

        try
        {
            Assert.False(result.HasErrors);

            using var loader = BinaryLoader.Load(binPath);

            ReadOnlySpan<int> authorSlotArray = loader.GetSlotArray("Authors");
            Assert.Equal(2, authorSlotArray.Length);
            Assert.Equal(1, authorSlotArray[0]); // Low UUID row is second in table, mapped key 0.
            Assert.Equal(0, authorSlotArray[1]); // High UUID row is first in table, mapped key 1.

            ReadOnlySpan<ExportTestUuidBookRow> books = loader.GetRecords<ExportTestUuidBookRow>("Books");
            Assert.Equal(2, books.Length);
            Assert.Equal(1, books[0].Author);
            Assert.Equal(0, books[1].Author);

            var firstAuthor = loader.FindById<ExportTestUuidAuthorRow>("Authors", 0);
            var secondAuthor = loader.FindById<ExportTestUuidAuthorRow>("Authors", 1);
            Assert.Equal("Low", firstAuthor.Name.ToString());
            Assert.Equal("High", secondAuthor.Name.ToString());
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Export_PrimaryKey_IdColumn_DuplicateUuid_IsHardError()
    {
        var project = BuildUuidPrimaryKeyRelationProject(out var authors);
        DocColumn authorUuidColumn = authors.Columns[0];

        // Duplicate UUID value across two rows.
        authors.Rows[1].SetCell(authorUuidColumn.Id, DocCellValue.Text("00000000-0000-0000-0000-000000000010"));

        var (result, binPath, _) = ExportToTemp(project);

        try
        {
            Assert.True(result.HasErrors);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "export/key/pk-duplicate");
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Export_RoundTrip_QueryApis_Work()
    {
        var project = BuildUnitsProject(out var units);
        var (result, binPath, generatedDir) = ExportToTemp(project);

        try
        {
            Assert.False(result.HasErrors);
            Assert.True(Directory.Exists(generatedDir));

            var sources = new List<string>(result.GeneratedFiles.Count + 1);
            for (int i = 0; i < result.GeneratedFiles.Count; i++)
            {
                sources.Add(result.GeneratedFiles[i].Content);
            }
            sources.Add(RenderHarnessSource());

            var extraRefs = new[]
            {
                MetadataReference.CreateFromFile(typeof(Core.StringHandle).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(FixedMath.Fixed64).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(BinaryLoader).Assembly.Location),
            };

            var assembly = RoslynCompileHelper.CompileToAssembly("DerpDoc.Phase5.Generated", sources, extraRefs);
            var harness = assembly.GetType("DerpDocDatabase.ExportTestHarness", throwOnError: true)!;

            var getName = harness.GetMethod("GetUnitName", BindingFlags.Public | BindingFlags.Static)!;
            var name = (string)getName.Invoke(null, [binPath, 1])!;
            Assert.Equal("Tank", name);

            var getFactionIds = harness.GetMethod("GetUnitIdsByFaction", BindingFlags.Public | BindingFlags.Static)!;
            var factionIds = (int[])getFactionIds.Invoke(null, [binPath, 0])!;
            Assert.Equal([0], factionIds);

            var getRangeIds = harness.GetMethod("GetUnitIdsByIdRange", BindingFlags.Public | BindingFlags.Static)!;
            var rangeIds = (int[])getRangeIds.Invoke(null, [binPath, 0, 1])!;
            Assert.Equal([0, 1], rangeIds);
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Generated_GameDatabase_AssignsTables_After_AllWrappers_Are_Built()
    {
        var project = BuildTwoTableStringProject(out _, out _);
        var (result, binPath, _) = ExportToTemp(project);

        try
        {
            Assert.False(result.HasErrors);

            string gameDatabaseSource = string.Empty;
            for (int fileIndex = 0; fileIndex < result.GeneratedFiles.Count; fileIndex++)
            {
                if (string.Equals(result.GeneratedFiles[fileIndex].FileName, "GameDatabase.g.cs", StringComparison.Ordinal))
                {
                    gameDatabaseSource = result.GeneratedFiles[fileIndex].Content;
                    break;
                }
            }

            Assert.False(string.IsNullOrWhiteSpace(gameDatabaseSource));

            int unitsCreateIndex = gameDatabaseSource.IndexOf("var unitsTable = CreateUnitsTable(loader, \"Units\");", StringComparison.Ordinal);
            int buildingsCreateIndex = gameDatabaseSource.IndexOf("var buildingsTable = CreateBuildingsTable(loader, \"Buildings\");", StringComparison.Ordinal);
            int unitsAssignIndex = gameDatabaseSource.IndexOf("Units = unitsTable;", StringComparison.Ordinal);
            int buildingsAssignIndex = gameDatabaseSource.IndexOf("Buildings = buildingsTable;", StringComparison.Ordinal);
            int unitsRuntimeAssignIndex = gameDatabaseSource.IndexOf("UnitsRuntime = new UnitsRuntime(Units);", StringComparison.Ordinal);
            int buildingsRuntimeAssignIndex = gameDatabaseSource.IndexOf("BuildingsRuntime = new BuildingsRuntime(Buildings);", StringComparison.Ordinal);
            int unitsConnectIndex = gameDatabaseSource.IndexOf("UnitsRuntime.ConnectRuntimes(BuildingsRuntime);", StringComparison.Ordinal);
            int buildingsConnectIndex = gameDatabaseSource.IndexOf("BuildingsRuntime.ConnectRuntimes(UnitsRuntime);", StringComparison.Ordinal);

            Assert.True(unitsCreateIndex >= 0);
            Assert.True(buildingsCreateIndex >= 0);
            Assert.True(unitsAssignIndex >= 0);
            Assert.True(buildingsAssignIndex >= 0);
            Assert.True(unitsRuntimeAssignIndex >= 0);
            Assert.True(buildingsRuntimeAssignIndex >= 0);
            Assert.True(unitsConnectIndex >= 0);
            Assert.True(buildingsConnectIndex >= 0);

            Assert.True(unitsAssignIndex > unitsCreateIndex);
            Assert.True(buildingsAssignIndex > buildingsCreateIndex);
            Assert.True(unitsRuntimeAssignIndex > unitsAssignIndex);
            Assert.True(buildingsRuntimeAssignIndex > buildingsAssignIndex);
            Assert.True(unitsConnectIndex > buildingsRuntimeAssignIndex);
            Assert.True(buildingsConnectIndex > buildingsRuntimeAssignIndex);

            Assert.Equal(-1, gameDatabaseSource.IndexOf("Units = new UnitsTable(", StringComparison.Ordinal));
            Assert.Equal(-1, gameDatabaseSource.IndexOf("Buildings = new BuildingsTable(", StringComparison.Ordinal));
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Export_AssetColumns_UseStringHandleMapping()
    {
        var project = new DocProject { Name = "AssetExport" };
        var table = new DocTable
        {
            Name = "Visuals",
            FileName = "visuals",
            ExportConfig = new DocTableExportConfig { Enabled = true },
        };

        var idColumn = new DocColumn { Name = "Id", Kind = DocColumnKind.Number, ExportType = "int" };
        var textureColumn = new DocColumn { Name = "Texture", Kind = DocColumnKind.TextureAsset };
        var meshColumn = new DocColumn { Name = "Mesh", Kind = DocColumnKind.MeshAsset };
        var voiceColumn = new DocColumn { Name = "Voice", Kind = DocColumnKind.AudioAsset };
        table.Columns.Add(idColumn);
        table.Columns.Add(textureColumn);
        table.Columns.Add(meshColumn);
        table.Columns.Add(voiceColumn);
        table.Keys.PrimaryKeyColumnId = idColumn.Id;

        var row = new DocRow { Id = "visual-row" };
        row.SetCell(idColumn.Id, DocCellValue.Number(1));
        row.SetCell(textureColumn.Id, DocCellValue.Text("Textures/hero.png"));
        row.SetCell(meshColumn.Id, DocCellValue.Text("Meshes/tree.glb"));
        row.SetCell(voiceColumn.Id, DocCellValue.Text("Audio/hero_voice.mp3"));
        table.Rows.Add(row);

        project.Tables.Add(table);

        var (result, binPath, _) = ExportToTemp(project);
        try
        {
            Assert.False(result.HasErrors);

            bool hasTextureStringHandle = false;
            bool hasMeshStringHandle = false;
            bool hasVoiceStringHandle = false;
            for (int fileIndex = 0; fileIndex < result.GeneratedFiles.Count; fileIndex++)
            {
                string source = result.GeneratedFiles[fileIndex].Content;
                if (source.Contains("public StringHandle Texture;", StringComparison.Ordinal))
                {
                    hasTextureStringHandle = true;
                }

                if (source.Contains("public StringHandle Mesh;", StringComparison.Ordinal))
                {
                    hasMeshStringHandle = true;
                }

                if (source.Contains("public StringHandle Voice;", StringComparison.Ordinal))
                {
                    hasVoiceStringHandle = true;
                }
            }

            Assert.True(hasTextureStringHandle);
            Assert.True(hasMeshStringHandle);
            Assert.True(hasVoiceStringHandle);

            using var loader = BinaryLoader.Load(binPath);
            var rows = loader.GetRecords<ExportTestAssetRow>("Visuals");
            Assert.Equal(1, rows.Length);
            Assert.Equal("Textures/hero.png", rows[0].Texture.ToString());
            Assert.Equal("Meshes/tree.glb", rows[0].Mesh.ToString());
            Assert.Equal("Audio/hero_voice.mp3", rows[0].Voice.ToString());
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Export_NativeTypedColumns_AreMappedWithoutUnsupportedErrors()
    {
        var project = BuildTypedColumnsProject();
        var (result, binPath, _) = ExportToTemp(project);

        try
        {
            Assert.False(result.HasErrors);
            Assert.DoesNotContain(result.Diagnostics, d => d.Code == "export/type/unsupported-column-kind");

            string rowSource = string.Empty;
            for (int fileIndex = 0; fileIndex < result.GeneratedFiles.Count; fileIndex++)
            {
                if (string.Equals(result.GeneratedFiles[fileIndex].FileName, "TypedRows.g.cs", StringComparison.Ordinal))
                {
                    rowSource = result.GeneratedFiles[fileIndex].Content;
                    break;
                }
            }

            Assert.False(string.IsNullOrWhiteSpace(rowSource));
            Assert.Contains("public Fixed64Vec2 Position;", rowSource, StringComparison.Ordinal);
            Assert.Contains("public Fixed32Vec3 Direction;", rowSource, StringComparison.Ordinal);
            Assert.Contains("public Fixed64Vec4 ColorHdr;", rowSource, StringComparison.Ordinal);
            Assert.Contains("public Fixed32Vec4 Blend;", rowSource, StringComparison.Ordinal);
            Assert.Contains("public Color32 Tint;", rowSource, StringComparison.Ordinal);
            Assert.Contains("public SplineHandle Curve;", rowSource, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Export_SubtableImplicitChild_ScopedNavigation_Works()
    {
        var project = BuildSubtableNavigationProject();
        var (result, binPath, _) = ExportToTemp(project);

        try
        {
            Assert.False(result.HasErrors);

            var sources = new List<string>(result.GeneratedFiles.Count + 1);
            for (int fileIndex = 0; fileIndex < result.GeneratedFiles.Count; fileIndex++)
            {
                sources.Add(result.GeneratedFiles[fileIndex].Content);
            }
            sources.Add(RenderSubtableHarnessSource());

            var extraRefs = new[]
            {
                MetadataReference.CreateFromFile(typeof(Core.StringHandle).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(FixedMath.Fixed64).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(BinaryLoader).Assembly.Location),
            };

            var assembly = RoslynCompileHelper.CompileToAssembly("DerpDoc.Phase5.SubtableHarness", sources, extraRefs);
            var harness = assembly.GetType("DerpDocDatabase.SubtableHarness", throwOnError: true)!;

            var readValues = harness.GetMethod("ReadChildValues", BindingFlags.Public | BindingFlags.Static)!;
            var readParent = harness.GetMethod("ResolveParentId", BindingFlags.Public | BindingFlags.Static)!;

            var valuesForParent10 = (int[])readValues.Invoke(null, [binPath, 10])!;
            var valuesForParent20 = (int[])readValues.Invoke(null, [binPath, 20])!;
            int resolvedParent = (int)readParent.Invoke(null, [binPath, 10])!;

            Assert.Equal([101, 102], valuesForParent10);
            Assert.Equal([201], valuesForParent20);
            Assert.Equal(10, resolvedParent);
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Export_RowReference_Subtable_GlobalAndTypedFindById_Work()
    {
        var project = BuildRowReferenceSubtableProject();
        var (result, binPath, _) = ExportToTemp(project);

        try
        {
            Assert.False(result.HasErrors);

            var sources = new List<string>(result.GeneratedFiles.Count + 1);
            for (int fileIndex = 0; fileIndex < result.GeneratedFiles.Count; fileIndex++)
            {
                sources.Add(result.GeneratedFiles[fileIndex].Content);
            }
            sources.Add(RenderRowRefHarnessSource());

            var extraRefs = new[]
            {
                MetadataReference.CreateFromFile(typeof(Core.StringHandle).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(FixedMath.Fixed64).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(BinaryLoader).Assembly.Location),
            };

            var assembly = RoslynCompileHelper.CompileToAssembly("DerpDoc.Phase5.RowRefHarness", sources, extraRefs);
            var harness = assembly.GetType("DerpDocDatabase.RowRefHarness", throwOnError: true)!;
            var evaluate = harness.GetMethod("Evaluate", BindingFlags.Public | BindingFlags.Static)!;
            var values = (int[])evaluate.Invoke(null, [binPath])!;

            Assert.Equal(2, values[0]); // global FindById(7) returns all matching placements.
            Assert.Equal(2, values[1]); // typed Enemies.FindById(7) returns both enemy placements.
            Assert.Equal(1, values[2]); // typed Player.FindById(1)
            Assert.Equal(1, values[3]); // typed Triggers.FindById(50)
            Assert.Equal(10, values[4]); // scoped placement resolves parent row.
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Export_GeneratedRuntime_TableVariables_Bindings_And_Links_Work()
    {
        var project = BuildRuntimeBindingProject(out _, out _);
        var (result, binPath, _) = ExportToTemp(project);

        try
        {
            Assert.False(result.HasErrors);

            var sources = new List<string>(result.GeneratedFiles.Count + 1);
            for (int fileIndex = 0; fileIndex < result.GeneratedFiles.Count; fileIndex++)
            {
                sources.Add(result.GeneratedFiles[fileIndex].Content);
            }
            sources.Add(RenderRuntimeBindingHarnessSource());

            var extraRefs = new[]
            {
                MetadataReference.CreateFromFile(typeof(Core.StringHandle).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(FixedMath.Fixed64).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(BinaryLoader).Assembly.Location),
            };

            var assembly = RoslynCompileHelper.CompileToAssembly("DerpDoc.Phase5.RuntimeBindings", sources, extraRefs);
            var harness = assembly.GetType("DerpDocDatabase.RuntimeBindingHarness", throwOnError: true)!;
            var evaluate = harness.GetMethod("Evaluate", BindingFlags.Public | BindingFlags.Static)!;
            var values = (double[])evaluate.Invoke(null, [binPath])!;

            Assert.Equal(20d, values[0]);
            Assert.Equal(3d, values[1]);
            Assert.Equal(20d, values[2]);
            Assert.Equal(3d, values[3]);
            Assert.Equal(5d, values[4]);
            Assert.Equal(9d, values[5]);
            Assert.Equal(5d, values[6]);
            Assert.Equal(1d, values[7]);
            Assert.True(values[8] > 0d);
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Export_ViewBinding_TypeMismatch_IsHardError()
    {
        var project = BuildRuntimeBindingProject(out _, out var people);
        for (int variableIndex = 0; variableIndex < people.Variables.Count; variableIndex++)
        {
            if (string.Equals(people.Variables[variableIndex].Name, "filter_value", StringComparison.OrdinalIgnoreCase))
            {
                people.Variables[variableIndex].Kind = DocColumnKind.Checkbox;
            }
        }

        var (result, binPath, _) = ExportToTemp(project);

        try
        {
            Assert.True(result.HasErrors);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "export/bindings/type-mismatch");
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Binary_StringRegistry_SharedAcrossTables()
    {
        var project = BuildTwoTableStringProject(out _, out _);
        var (result, binPath, _) = ExportToTemp(project);

        try
        {
            Assert.False(result.HasErrors);

            var bytes = File.ReadAllBytes(binPath);
            uint stringRegistryOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16, 4));
            uint stringRegistryCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20, 4));
            Assert.True(stringRegistryOffset > 0);
            Assert.Equal(1u, stringRegistryCount);

            using var loader = BinaryLoader.Load(binPath);
            var units = loader.GetRecords<ExportTestNameRow>("Units");
            var buildings = loader.GetRecords<ExportTestNameRow>("Buildings");
            Assert.Equal(1, units.Length);
            Assert.Equal(1, buildings.Length);
            Assert.Equal(units[0].Name, buildings[0].Name);
            Assert.Equal("Shared", units[0].Name.ToString());
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Binary_EmptyTable_RoundTrips()
    {
        var project = BuildEmptyProject();
        var (result, binPath, _) = ExportToTemp(project);

        try
        {
            Assert.False(result.HasErrors);
            using var loader = BinaryLoader.Load(binPath);
            Assert.Equal(0, loader.GetRecordCount("Empty"));
            Assert.True(loader.GetRecords<ExportTestNameRow>("Empty").IsEmpty);
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Export_Variants_AreWritten_AsVariantTables()
    {
        var project = BuildUnitsVariantProject();
        var (result, binPath, _) = ExportToTemp(project);

        try
        {
            Assert.False(result.HasErrors);

            using var loader = BinaryLoader.Load(binPath);
            Assert.Equal(2, loader.GetRecordCount("Units"));
            Assert.Equal(2, loader.GetRecordCount("Units@v1"));

            var baseRows = loader.GetRecords<ExportTestUnitData>("Units");
            var variantRows = loader.GetRecords<ExportTestUnitData>("Units@v1");

            Assert.Equal("Marine", baseRows[0].Name.ToString());
            Assert.Equal("Tank", baseRows[1].Name.ToString());

            Assert.Equal("MarineElite", variantRows[0].Name.ToString());
            Assert.Equal(2, variantRows[1].Id);
            Assert.Equal("Artillery", variantRows[1].Name.ToString());
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Generated_GameDatabase_IncludesVariantSelectionApis()
    {
        var project = BuildUnitsVariantProject();
        var (result, binPath, _) = ExportToTemp(project);

        try
        {
            Assert.False(result.HasErrors);

            string gameDatabaseSource = string.Empty;
            for (int fileIndex = 0; fileIndex < result.GeneratedFiles.Count; fileIndex++)
            {
                if (string.Equals(result.GeneratedFiles[fileIndex].FileName, "GameDatabase.g.cs", StringComparison.Ordinal))
                {
                    gameDatabaseSource = result.GeneratedFiles[fileIndex].Content;
                    break;
                }
            }

            Assert.False(string.IsNullOrWhiteSpace(gameDatabaseSource));
            Assert.Contains("TryGetUnitsVariant", gameDatabaseSource, StringComparison.Ordinal);
            Assert.Contains("UnitsVariants", gameDatabaseSource, StringComparison.Ordinal);
            Assert.Contains("MarinePlus", gameDatabaseSource, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Manifest_IncludesVariantNames_AndVariantTableRows()
    {
        var project = BuildUnitsVariantProject();

        string root = Path.Combine(Path.GetTempPath(), "DerpDocPhase5Manifest_" + Guid.NewGuid().ToString("N"));
        string generatedDir = Path.Combine(root, "gen");
        string binPath = Path.Combine(root, "Test.derpdoc");

        var options = new ExportPipelineOptions
        {
            GeneratedOutputDirectory = generatedDir,
            BinaryOutputPath = binPath,
            WriteManifest = true,
        };

        var pipeline = new DocExportPipeline();
        var result = pipeline.Export(project, options);

        try
        {
            Assert.False(result.HasErrors);

            string manifestPath = binPath + ".manifest.json";
            Assert.True(File.Exists(manifestPath));
            string json = File.ReadAllText(manifestPath);
            DerpDocManifest? manifest = JsonSerializer.Deserialize<DerpDocManifest>(json);
            Assert.NotNull(manifest);

            DerpDocManifestTable? unitsTable = manifest.Tables.Find(table => string.Equals(table.Name, "Units", StringComparison.Ordinal));
            Assert.NotNull(unitsTable);
            Assert.Contains(unitsTable!.Variants, variant => variant.Id == 0 && variant.VariantName == "Base" && variant.TableName == "Units" && variant.RowCount == 2u);
            Assert.Contains(unitsTable.Variants, variant => variant.Id == 1 && variant.VariantName == "MarinePlus" && variant.TableName == "Units@v1" && variant.RowCount == 2u);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Export_FormulaColumn_BakedValue()
    {
        var project = BuildFormulaProject();
        var (result, binPath, _) = ExportToTemp(project);

        try
        {
            Assert.False(result.HasErrors);
            using var loader = BinaryLoader.Load(binPath);
            var rows = loader.GetRecords<ExportTestFormulaRow>("Metrics");
            Assert.Equal(1, rows.Length);
            Assert.Equal(10, rows[0].Value);
            Assert.Equal(11, rows[0].PlusOne);
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Export_Derived_MultiMatch_IsHardError()
    {
        var project = BuildDerivedMultiMatchProject();
        var (result, binPath, _) = ExportToTemp(project);

        try
        {
            Assert.True(result.HasErrors);
            Assert.Contains(result.Diagnostics, d => d.Code == "export/derived/multimatch");
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Export_FormulaError_IsHardError()
    {
        var project = BuildFormulaErrorProject();
        var (result, binPath, _) = ExportToTemp(project);

        try
        {
            Assert.True(result.HasErrors);
            Assert.Contains(result.Diagnostics, d => d.Code == "export/formula/error");
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Determinism_ExportBytes_AreIdentical()
    {
        var project = BuildUnitsProject(out _);

        var (first, firstBin, _) = ExportToTemp(project);
        var firstBytes = File.ReadAllBytes(firstBin);

        TryDeleteDirectory(Path.GetDirectoryName(firstBin)!);

        var (second, secondBin, _) = ExportToTemp(project);
        var secondBytes = File.ReadAllBytes(secondBin);

        try
        {
            Assert.False(first.HasErrors);
            Assert.False(second.HasErrors);
            Assert.Equal(firstBytes, secondBytes);
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(secondBin)!);
        }
    }

    [Fact]
    public void LiveBinary_DoubleBuffer_ActiveSlot_RoundTrips()
    {
        var project = BuildUnitsProject(out _);
        var (result, binPath, _) = ExportToTemp(project);

        try
        {
            Assert.False(result.HasErrors);

            string root = Path.GetDirectoryName(binPath)!;
            string livePath = Path.Combine(root, ".derpdoc-live.bin");
            int tableCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(result.Binary.AsSpan(8, 4));

            using var writer = LiveBinaryWriter.CreateOrOpen(livePath, tableCount, result.Binary.Length);
            writer.Write(result.Binary);

            using var reader = LiveBinaryReader.Open(livePath);
            var header1 = reader.ReadHeader();
            Assert.Equal(LiveBinaryHeader.MagicValue, header1.Magic);
            Assert.Equal(1, header1.ActiveSlot);
            Assert.Equal(1u, header1.Generation);

            using (var slotLoader = reader.LoadActiveSlot())
            {
                Assert.Equal(2, slotLoader.GetRecordCount("Units"));
            }

            writer.Write(result.Binary);

            var header2 = reader.ReadHeader();
            Assert.Equal(0, header2.ActiveSlot);
            Assert.Equal(2u, header2.Generation);

            using (var slotLoader = reader.LoadActiveSlot())
            {
                Assert.Equal(2, slotLoader.GetRecordCount("Units"));
                var tank = slotLoader.FindById<ExportTestUnitData>("Units", 1);
                Assert.Equal("Tank", tank.Name.ToString());
            }
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    [Fact]
    public void Export_Writes_LiveBinary_And_Preserves_Generation_Across_Exports()
    {
        var project = BuildUnitsProject(out _);
        var (firstResult, binPath, _, livePath) = ExportToTempWithLive(project);

        try
        {
            Assert.False(firstResult.HasErrors);
            Assert.True(File.Exists(livePath));

            using var firstReader = LiveBinaryReader.Open(livePath);
            var firstHeader = firstReader.ReadHeader();
            Assert.Equal(1u, firstHeader.Generation);

            var pipeline = new DocExportPipeline();
            var secondResult = pipeline.Export(project, new ExportPipelineOptions
            {
                GeneratedOutputDirectory = "",
                BinaryOutputPath = binPath,
                LiveBinaryOutputPath = livePath,
                WriteManifest = false,
            });

            Assert.False(secondResult.HasErrors);

            using var secondReader = LiveBinaryReader.Open(livePath);
            var secondHeader = secondReader.ReadHeader();
            Assert.Equal(2u, secondHeader.Generation);
            Assert.NotEqual(firstHeader.ActiveSlot, secondHeader.ActiveSlot);
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(binPath)!);
        }
    }

    private static (ExportPipelineResult Result, string BinPath, string GeneratedDir) ExportToTemp(DocProject project)
    {
        string root = Path.Combine(Path.GetTempPath(), "DerpDocPhase5_" + Guid.NewGuid().ToString("N"));
        string generatedDir = Path.Combine(root, "gen");
        string binPath = Path.Combine(root, "Test.derpdoc");

        var options = new ExportPipelineOptions
        {
            GeneratedOutputDirectory = generatedDir,
            BinaryOutputPath = binPath,
            WriteManifest = false,
        };

        var pipeline = new DocExportPipeline();
        var result = pipeline.Export(project, options);
        return (result, binPath, generatedDir);
    }

    private static (ExportPipelineResult Result, string BinPath, string GeneratedDir, string LivePath) ExportToTempWithLive(DocProject project)
    {
        string root = Path.Combine(Path.GetTempPath(), "DerpDocPhase5Live_" + Guid.NewGuid().ToString("N"));
        string generatedDir = Path.Combine(root, "gen");
        string binPath = Path.Combine(root, "Test.derpdoc");
        string livePath = Path.Combine(root, ".derpdoc-live.bin");

        var options = new ExportPipelineOptions
        {
            GeneratedOutputDirectory = generatedDir,
            BinaryOutputPath = binPath,
            LiveBinaryOutputPath = livePath,
            WriteManifest = false,
        };

        var pipeline = new DocExportPipeline();
        var result = pipeline.Export(project, options);
        return (result, binPath, generatedDir, livePath);
    }

    private static DocProject BuildUnitsProject(out DocTable units)
    {
        var project = new DocProject { Name = "Test" };

        units = new DocTable
        {
            Name = "Units",
            FileName = "units",
            ExportConfig = new DocTableExportConfig
            {
                Enabled = true,
            },
        };

        var idColumn = new DocColumn { Name = "Id", Kind = DocColumnKind.Number, ExportType = "int" };
        var nameColumn = new DocColumn { Name = "Name", Kind = DocColumnKind.Text };
        var codeColumn = new DocColumn { Name = "Code", Kind = DocColumnKind.Number, ExportType = "int" };
        var factionColumn = new DocColumn
        {
            Name = "Faction",
            Kind = DocColumnKind.Select,
            Options = ["Red", "Blue"],
        };

        units.Columns.Add(idColumn);
        units.Columns.Add(nameColumn);
        units.Columns.Add(codeColumn);
        units.Columns.Add(factionColumn);

        units.Keys.PrimaryKeyColumnId = idColumn.Id;
        units.Keys.SecondaryKeys.Add(new DocSecondaryKey { ColumnId = codeColumn.Id, Unique = true });
        units.Keys.SecondaryKeys.Add(new DocSecondaryKey { ColumnId = factionColumn.Id, Unique = false });

        var row0 = new DocRow { Id = "r0" };
        row0.SetCell(idColumn.Id, DocCellValue.Number(0));
        row0.SetCell(nameColumn.Id, DocCellValue.Text("Marine"));
        row0.SetCell(codeColumn.Id, DocCellValue.Number(100));
        row0.SetCell(factionColumn.Id, DocCellValue.Text("Red"));
        units.Rows.Add(row0);

        var row1 = new DocRow { Id = "r1" };
        row1.SetCell(idColumn.Id, DocCellValue.Number(1));
        row1.SetCell(nameColumn.Id, DocCellValue.Text("Tank"));
        row1.SetCell(codeColumn.Id, DocCellValue.Number(101));
        row1.SetCell(factionColumn.Id, DocCellValue.Text("Blue"));
        units.Rows.Add(row1);

        project.Tables.Add(units);
        return project;
    }

    private static DocProject BuildTwoTableStringProject(out DocTable units, out DocTable buildings)
    {
        var project = new DocProject { Name = "TwoTables" };

        units = new DocTable
        {
            Name = "Units",
            FileName = "units",
            ExportConfig = new DocTableExportConfig { Enabled = true },
        };
        var unitId = new DocColumn { Name = "Id", Kind = DocColumnKind.Number, ExportType = "int" };
        var unitName = new DocColumn { Name = "Name", Kind = DocColumnKind.Text };
        units.Columns.Add(unitId);
        units.Columns.Add(unitName);
        units.Keys.PrimaryKeyColumnId = unitId.Id;
        var unitRow = new DocRow { Id = "u0" };
        unitRow.SetCell(unitId.Id, DocCellValue.Number(0));
        unitRow.SetCell(unitName.Id, DocCellValue.Text("Shared"));
        units.Rows.Add(unitRow);

        buildings = new DocTable
        {
            Name = "Buildings",
            FileName = "buildings",
            ExportConfig = new DocTableExportConfig { Enabled = true },
        };
        var buildingId = new DocColumn { Name = "Id", Kind = DocColumnKind.Number, ExportType = "int" };
        var buildingName = new DocColumn { Name = "Name", Kind = DocColumnKind.Text };
        buildings.Columns.Add(buildingId);
        buildings.Columns.Add(buildingName);
        buildings.Keys.PrimaryKeyColumnId = buildingId.Id;
        var buildingRow = new DocRow { Id = "b0" };
        buildingRow.SetCell(buildingId.Id, DocCellValue.Number(0));
        buildingRow.SetCell(buildingName.Id, DocCellValue.Text("Shared"));
        buildings.Rows.Add(buildingRow);

        project.Tables.Add(units);
        project.Tables.Add(buildings);
        return project;
    }

    private static DocProject BuildUuidPrimaryKeyRelationProject(out DocTable authors)
    {
        var project = new DocProject { Name = "UuidPk" };

        authors = new DocTable
        {
            Name = "Authors",
            FileName = "authors",
            ExportConfig = new DocTableExportConfig { Enabled = true },
        };

        var authorUuid = new DocColumn { Name = "AuthorUuid", Kind = DocColumnKind.Id };
        var authorName = new DocColumn { Name = "Name", Kind = DocColumnKind.Text };
        authors.Columns.Add(authorUuid);
        authors.Columns.Add(authorName);
        authors.Keys.PrimaryKeyColumnId = authorUuid.Id;

        // Intentionally add rows in non-sorted UUID order so mapping proves row-order independence.
        var highRow = new DocRow { Id = "author_high" };
        highRow.SetCell(authorUuid.Id, DocCellValue.Text("00000000-0000-0000-0000-000000000010"));
        highRow.SetCell(authorName.Id, DocCellValue.Text("High"));
        authors.Rows.Add(highRow);

        var lowRow = new DocRow { Id = "author_low" };
        lowRow.SetCell(authorUuid.Id, DocCellValue.Text("00000000-0000-0000-0000-000000000001"));
        lowRow.SetCell(authorName.Id, DocCellValue.Text("Low"));
        authors.Rows.Add(lowRow);

        var books = new DocTable
        {
            Name = "Books",
            FileName = "books",
            ExportConfig = new DocTableExportConfig { Enabled = true },
        };

        var bookId = new DocColumn { Name = "Id", Kind = DocColumnKind.Number, ExportType = "int" };
        var bookTitle = new DocColumn { Name = "Title", Kind = DocColumnKind.Text };
        var bookAuthor = new DocColumn
        {
            Name = "Author",
            Kind = DocColumnKind.Relation,
            RelationTableId = authors.Id,
            RelationDisplayColumnId = authorName.Id,
        };
        books.Columns.Add(bookId);
        books.Columns.Add(bookTitle);
        books.Columns.Add(bookAuthor);
        books.Keys.PrimaryKeyColumnId = bookId.Id;

        var bookA = new DocRow { Id = "book_a" };
        bookA.SetCell(bookId.Id, DocCellValue.Number(0));
        bookA.SetCell(bookTitle.Id, DocCellValue.Text("First"));
        bookA.SetCell(bookAuthor.Id, DocCellValue.Text(highRow.Id));
        books.Rows.Add(bookA);

        var bookB = new DocRow { Id = "book_b" };
        bookB.SetCell(bookId.Id, DocCellValue.Number(1));
        bookB.SetCell(bookTitle.Id, DocCellValue.Text("Second"));
        bookB.SetCell(bookAuthor.Id, DocCellValue.Text(lowRow.Id));
        books.Rows.Add(bookB);

        project.Tables.Add(authors);
        project.Tables.Add(books);
        return project;
    }

    private static DocProject BuildRuntimeBindingProject(out DocTable settings, out DocTable people)
    {
        var project = new DocProject { Name = "RuntimeBindings" };

        settings = new DocTable
        {
            Name = "Settings",
            FileName = "settings",
            ExportConfig = new DocTableExportConfig { Enabled = true },
        };
        var settingsId = new DocColumn { Name = "Id", Kind = DocColumnKind.Number, ExportType = "int" };
        var settingsName = new DocColumn { Name = "Name", Kind = DocColumnKind.Text };
        settings.Columns.Add(settingsId);
        settings.Columns.Add(settingsName);
        settings.Keys.PrimaryKeyColumnId = settingsId.Id;
        settings.Variables.Add(new DocTableVariable
        {
            Name = "limit",
            Kind = DocColumnKind.Number,
            Expression = "5",
        });

        var settingsRow = new DocRow { Id = "settings_row" };
        settingsRow.SetCell(settingsId.Id, DocCellValue.Number(0));
        settingsRow.SetCell(settingsName.Id, DocCellValue.Text("defaults"));
        settings.Rows.Add(settingsRow);

        people = new DocTable
        {
            Name = "People",
            FileName = "people",
            ExportConfig = new DocTableExportConfig { Enabled = true },
        };
        var peopleId = new DocColumn { Name = "Id", Kind = DocColumnKind.Number, ExportType = "int" };
        var peopleStatus = new DocColumn { Name = "Status", Kind = DocColumnKind.Text };
        people.Columns.Add(peopleId);
        people.Columns.Add(peopleStatus);
        people.Keys.PrimaryKeyColumnId = peopleId.Id;

        people.Variables.Add(new DocTableVariable
        {
            Name = "limit",
            Kind = DocColumnKind.Number,
            Expression = "10",
        });
        people.Variables.Add(new DocTableVariable
        {
            Name = "effective_limit",
            Kind = DocColumnKind.Number,
            Expression = "thisTable.limit",
        });
        people.Variables.Add(new DocTableVariable
        {
            Name = "linked_limit",
            Kind = DocColumnKind.Number,
            Expression = "tables.Settings.limit",
        });
        people.Variables.Add(new DocTableVariable
        {
            Name = "filter_value",
            Kind = DocColumnKind.Text,
            Expression = "\"ready\"",
        });
        people.Variables.Add(new DocTableVariable
        {
            Name = "sort_desc",
            Kind = DocColumnKind.Checkbox,
            Expression = "false",
        });
        people.Variables.Add(new DocTableVariable
        {
            Name = "filter_column",
            Kind = DocColumnKind.Text,
            Expression = "\"" + peopleStatus.Id + "\"",
        });

        var view = new DocView
        {
            Name = "Main",
            Type = DocViewType.Grid,
        };
        view.Filters.Add(new DocViewFilter
        {
            ColumnId = peopleStatus.Id,
            Op = DocViewFilterOp.Equals,
            Value = "ready",
            ColumnIdBinding = new DocViewBinding
            {
                FormulaExpression = "thisTable.filter_column",
            },
            ValueBinding = new DocViewBinding
            {
                FormulaExpression = "thisTable.filter_value",
            },
        });
        view.Sorts.Add(new DocViewSort
        {
            ColumnId = peopleId.Id,
            Descending = false,
            DescendingBinding = new DocViewBinding
            {
                FormulaExpression = "thisTable.sort_desc",
            },
        });
        people.Views.Add(view);

        var peopleRow = new DocRow { Id = "people_row" };
        peopleRow.SetCell(peopleId.Id, DocCellValue.Number(0));
        peopleRow.SetCell(peopleStatus.Id, DocCellValue.Text("ready"));
        people.Rows.Add(peopleRow);

        project.Tables.Add(settings);
        project.Tables.Add(people);
        return project;
    }

    private static DocProject BuildTypedColumnsProject()
    {
        var project = new DocProject { Name = "TypedColumns" };
        var table = new DocTable
        {
            Name = "TypedRows",
            FileName = "typed_rows",
            ExportConfig = new DocTableExportConfig { Enabled = true },
        };

        var idColumn = new DocColumn { Name = "Id", Kind = DocColumnKind.Number, ExportType = "int" };
        var positionColumn = new DocColumn { Name = "Position", Kind = DocColumnKind.Vec2, ColumnTypeId = DocColumnTypeIds.Vec2Fixed64 };
        var directionColumn = new DocColumn { Name = "Direction", Kind = DocColumnKind.Vec3, ColumnTypeId = DocColumnTypeIds.Vec3Fixed32 };
        var blendColumn = new DocColumn { Name = "Blend", Kind = DocColumnKind.Vec4, ColumnTypeId = DocColumnTypeIds.Vec4Fixed32 };
        var colorColumn = new DocColumn { Name = "Tint", Kind = DocColumnKind.Color, ColumnTypeId = DocColumnTypeIds.ColorLdr };
        var colorHdrColumn = new DocColumn { Name = "ColorHdr", Kind = DocColumnKind.Color, ColumnTypeId = DocColumnTypeIds.ColorHdr };
        var splineColumn = new DocColumn { Name = "Curve", Kind = DocColumnKind.Spline, ColumnTypeId = DocColumnTypeIds.Spline };

        table.Columns.Add(idColumn);
        table.Columns.Add(positionColumn);
        table.Columns.Add(directionColumn);
        table.Columns.Add(blendColumn);
        table.Columns.Add(colorColumn);
        table.Columns.Add(colorHdrColumn);
        table.Columns.Add(splineColumn);
        table.Keys.PrimaryKeyColumnId = idColumn.Id;

        var row = new DocRow { Id = Guid.NewGuid().ToString() };
        row.SetCell(idColumn.Id, DocCellValue.Number(1));
        row.SetCell(positionColumn.Id, DocCellValue.Vec2(10.25, -4.5));
        row.SetCell(directionColumn.Id, DocCellValue.Vec3(1.25, 2.5, 3.75));
        row.SetCell(blendColumn.Id, DocCellValue.Vec4(0.1, 0.2, 0.3, 0.4));
        row.SetCell(colorColumn.Id, DocCellValue.Color(0.25, 0.5, 0.75, 1.0));
        row.SetCell(colorHdrColumn.Id, DocCellValue.Color(1.25, 1.5, 2.0, 1.0));
        row.SetCell(splineColumn.Id, DocCellValue.Text(SplineUtils.DefaultSplineJson));
        table.Rows.Add(row);

        project.Tables.Add(table);
        return project;
    }

    private static DocProject BuildSubtableNavigationProject()
    {
        var project = new DocProject { Name = "SubtableNavigation" };

        var levels = new DocTable
        {
            Name = "Levels",
            FileName = "levels",
            ExportConfig = new DocTableExportConfig { Enabled = true },
        };
        var levelIdColumn = new DocColumn { Name = "Id", Kind = DocColumnKind.Number, ExportType = "int" };
        var levelNameColumn = new DocColumn { Name = "Name", Kind = DocColumnKind.Text };
        var itemsSubtableColumn = new DocColumn
        {
            Name = "Items",
            Kind = DocColumnKind.Subtable,
            ColumnTypeId = DocColumnTypeIds.Subtable,
        };
        levels.Columns.Add(levelIdColumn);
        levels.Columns.Add(levelNameColumn);
        levels.Columns.Add(itemsSubtableColumn);
        levels.Keys.PrimaryKeyColumnId = levelIdColumn.Id;

        var level10Row = new DocRow { Id = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1" };
        level10Row.SetCell(levelIdColumn.Id, DocCellValue.Number(10));
        level10Row.SetCell(levelNameColumn.Id, DocCellValue.Text("Alpha"));
        levels.Rows.Add(level10Row);

        var level20Row = new DocRow { Id = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb2" };
        level20Row.SetCell(levelIdColumn.Id, DocCellValue.Number(20));
        level20Row.SetCell(levelNameColumn.Id, DocCellValue.Text("Beta"));
        levels.Rows.Add(level20Row);

        var items = new DocTable
        {
            Name = "Levels_Items",
            FileName = "levels_items",
            ParentTableId = levels.Id,
            ParentRowColumnId = "_parentRowId",
        };
        var parentRowColumn = new DocColumn
        {
            Id = "_parentRowId",
            Name = "_parentRowId",
            Kind = DocColumnKind.Text,
            IsHidden = true,
        };
        var valueColumn = new DocColumn { Name = "Value", Kind = DocColumnKind.Number, ExportType = "int" };
        items.Columns.Add(parentRowColumn);
        items.Columns.Add(valueColumn);

        var itemRowA = new DocRow { Id = "11111111-1111-4111-8111-111111111111" };
        itemRowA.SetCell(parentRowColumn.Id, DocCellValue.Text(level10Row.Id));
        itemRowA.SetCell(valueColumn.Id, DocCellValue.Number(101));
        items.Rows.Add(itemRowA);

        var itemRowB = new DocRow { Id = "22222222-2222-4222-8222-222222222222" };
        itemRowB.SetCell(parentRowColumn.Id, DocCellValue.Text(level10Row.Id));
        itemRowB.SetCell(valueColumn.Id, DocCellValue.Number(102));
        items.Rows.Add(itemRowB);

        var itemRowC = new DocRow { Id = "33333333-3333-4333-8333-333333333333" };
        itemRowC.SetCell(parentRowColumn.Id, DocCellValue.Text(level20Row.Id));
        itemRowC.SetCell(valueColumn.Id, DocCellValue.Number(201));
        items.Rows.Add(itemRowC);

        itemsSubtableColumn.SubtableId = items.Id;

        project.Tables.Add(levels);
        project.Tables.Add(items);
        return project;
    }

    private static DocProject BuildRowReferenceSubtableProject()
    {
        var project = new DocProject { Name = "RowRefNavigation" };

        var enemies = new DocTable
        {
            Name = "Enemies",
            FileName = "enemies",
            ExportConfig = new DocTableExportConfig { Enabled = true },
        };
        var enemyIdColumn = new DocColumn { Name = "Id", Kind = DocColumnKind.Number, ExportType = "int" };
        enemies.Columns.Add(enemyIdColumn);
        enemies.Keys.PrimaryKeyColumnId = enemyIdColumn.Id;
        var enemyRow = new DocRow { Id = "enemy-a" };
        enemyRow.SetCell(enemyIdColumn.Id, DocCellValue.Number(7));
        enemies.Rows.Add(enemyRow);

        var player = new DocTable
        {
            Name = "Player",
            FileName = "player",
            ExportConfig = new DocTableExportConfig { Enabled = true },
        };
        var playerIdColumn = new DocColumn { Name = "Id", Kind = DocColumnKind.Number, ExportType = "int" };
        player.Columns.Add(playerIdColumn);
        player.Keys.PrimaryKeyColumnId = playerIdColumn.Id;
        var playerRow = new DocRow { Id = "player-a" };
        playerRow.SetCell(playerIdColumn.Id, DocCellValue.Number(1));
        player.Rows.Add(playerRow);

        var triggers = new DocTable
        {
            Name = "Triggers",
            FileName = "triggers",
            ExportConfig = new DocTableExportConfig { Enabled = true },
        };
        var triggerIdColumn = new DocColumn { Name = "Id", Kind = DocColumnKind.Number, ExportType = "int" };
        triggers.Columns.Add(triggerIdColumn);
        triggers.Keys.PrimaryKeyColumnId = triggerIdColumn.Id;
        var triggerRow = new DocRow { Id = "trigger-a" };
        triggerRow.SetCell(triggerIdColumn.Id, DocCellValue.Number(50));
        triggers.Rows.Add(triggerRow);

        var levels = new DocTable
        {
            Name = "Levels",
            FileName = "levels",
            ExportConfig = new DocTableExportConfig { Enabled = true },
        };
        var levelIdColumn = new DocColumn { Name = "Id", Kind = DocColumnKind.Number, ExportType = "int" };
        var entitiesSubtableColumn = new DocColumn
        {
            Name = "Entities",
            Kind = DocColumnKind.Subtable,
            ColumnTypeId = DocColumnTypeIds.Subtable,
        };
        levels.Columns.Add(levelIdColumn);
        levels.Columns.Add(entitiesSubtableColumn);
        levels.Keys.PrimaryKeyColumnId = levelIdColumn.Id;
        var levelRow = new DocRow { Id = "level-a" };
        levelRow.SetCell(levelIdColumn.Id, DocCellValue.Number(10));
        levels.Rows.Add(levelRow);

        var entities = new DocTable
        {
            Name = "Levels_Entities",
            FileName = "levels_entities",
            ParentTableId = levels.Id,
            ParentRowColumnId = "_parentRowId",
        };
        var placementIdColumn = new DocColumn { Name = "PlacementId", Kind = DocColumnKind.Number, ExportType = "int" };
        var parentRowColumn = new DocColumn
        {
            Id = "_parentRowId",
            Name = "_parentRowId",
            Kind = DocColumnKind.Text,
            IsHidden = true,
        };
        var orderColumn = new DocColumn { Name = "Order", Kind = DocColumnKind.Number, ExportType = "int" };
        var tableRefColumn = new DocColumn
        {
            Name = "EntityTable",
            Kind = DocColumnKind.TableRef,
        };
        var rowIdColumn = new DocColumn
        {
            Name = "EntityRowId",
            Kind = DocColumnKind.Text,
            RowRefTableRefColumnId = tableRefColumn.Id,
        };
        entities.Columns.Add(placementIdColumn);
        entities.Columns.Add(parentRowColumn);
        entities.Columns.Add(orderColumn);
        entities.Columns.Add(tableRefColumn);
        entities.Columns.Add(rowIdColumn);
        entities.Keys.PrimaryKeyColumnId = placementIdColumn.Id;

        var placement100 = new DocRow { Id = "placement-100" };
        placement100.SetCell(placementIdColumn.Id, DocCellValue.Number(100));
        placement100.SetCell(parentRowColumn.Id, DocCellValue.Text(levelRow.Id));
        placement100.SetCell(orderColumn.Id, DocCellValue.Number(1));
        placement100.SetCell(tableRefColumn.Id, DocCellValue.Text(enemies.Id));
        placement100.SetCell(rowIdColumn.Id, DocCellValue.Text(enemyRow.Id));
        entities.Rows.Add(placement100);

        var placement101 = new DocRow { Id = "placement-101" };
        placement101.SetCell(placementIdColumn.Id, DocCellValue.Number(101));
        placement101.SetCell(parentRowColumn.Id, DocCellValue.Text(levelRow.Id));
        placement101.SetCell(orderColumn.Id, DocCellValue.Number(2));
        placement101.SetCell(tableRefColumn.Id, DocCellValue.Text(enemies.Id));
        placement101.SetCell(rowIdColumn.Id, DocCellValue.Text(enemyRow.Id));
        entities.Rows.Add(placement101);

        var placement102 = new DocRow { Id = "placement-102" };
        placement102.SetCell(placementIdColumn.Id, DocCellValue.Number(102));
        placement102.SetCell(parentRowColumn.Id, DocCellValue.Text(levelRow.Id));
        placement102.SetCell(orderColumn.Id, DocCellValue.Number(3));
        placement102.SetCell(tableRefColumn.Id, DocCellValue.Text(player.Id));
        placement102.SetCell(rowIdColumn.Id, DocCellValue.Text(playerRow.Id));
        entities.Rows.Add(placement102);

        var placement103 = new DocRow { Id = "placement-103" };
        placement103.SetCell(placementIdColumn.Id, DocCellValue.Number(103));
        placement103.SetCell(parentRowColumn.Id, DocCellValue.Text(levelRow.Id));
        placement103.SetCell(orderColumn.Id, DocCellValue.Number(4));
        placement103.SetCell(tableRefColumn.Id, DocCellValue.Text(triggers.Id));
        placement103.SetCell(rowIdColumn.Id, DocCellValue.Text(triggerRow.Id));
        entities.Rows.Add(placement103);

        entitiesSubtableColumn.SubtableId = entities.Id;

        project.Tables.Add(enemies);
        project.Tables.Add(player);
        project.Tables.Add(triggers);
        project.Tables.Add(levels);
        project.Tables.Add(entities);
        return project;
    }

    private static DocProject BuildEmptyProject()
    {
        var project = new DocProject { Name = "Empty" };
        var empty = new DocTable
        {
            Name = "Empty",
            FileName = "empty",
            ExportConfig = new DocTableExportConfig { Enabled = true },
        };
        var idColumn = new DocColumn { Name = "Id", Kind = DocColumnKind.Number, ExportType = "int" };
        var nameColumn = new DocColumn { Name = "Name", Kind = DocColumnKind.Text };
        empty.Columns.Add(idColumn);
        empty.Columns.Add(nameColumn);
        empty.Keys.PrimaryKeyColumnId = idColumn.Id;
        project.Tables.Add(empty);
        return project;
    }

    private static DocProject BuildFormulaProject()
    {
        var project = new DocProject { Name = "Formula" };
        var table = new DocTable
        {
            Name = "Metrics",
            FileName = "metrics",
            ExportConfig = new DocTableExportConfig { Enabled = true },
        };

        var idColumn = new DocColumn { Name = "Id", Kind = DocColumnKind.Number, ExportType = "int" };
        var valueColumn = new DocColumn { Name = "Value", Kind = DocColumnKind.Number, ExportType = "int" };
        var plusOneColumn = new DocColumn { Name = "PlusOne", Kind = DocColumnKind.Formula, ExportType = "int", FormulaExpression = "thisRow.Value + 1" };
        table.Columns.Add(idColumn);
        table.Columns.Add(valueColumn);
        table.Columns.Add(plusOneColumn);
        table.Keys.PrimaryKeyColumnId = idColumn.Id;

        var row = new DocRow { Id = "m0" };
        row.SetCell(idColumn.Id, DocCellValue.Number(0));
        row.SetCell(valueColumn.Id, DocCellValue.Number(10));
        table.Rows.Add(row);

        project.Tables.Add(table);
        return project;
    }

    private static DocProject BuildUnitsVariantProject()
    {
        var project = BuildUnitsProject(out var units);
        units.Variants.Add(new DocTableVariant
        {
            Id = 1,
            Name = "MarinePlus",
        });

        var variantDelta = new DocTableVariantDelta
        {
            VariantId = 1,
        };
        variantDelta.DeletedBaseRowIds.Add("r1");

        var addedRow = new DocRow { Id = "r2" };
        addedRow.SetCell(units.Columns[0].Id, DocCellValue.Number(2));
        addedRow.SetCell(units.Columns[1].Id, DocCellValue.Text("Artillery"));
        addedRow.SetCell(units.Columns[2].Id, DocCellValue.Number(102));
        addedRow.SetCell(units.Columns[3].Id, DocCellValue.Text("Blue"));
        variantDelta.AddedRows.Add(addedRow);

        variantDelta.CellOverrides.Add(new DocTableCellOverride
        {
            RowId = "r0",
            ColumnId = units.Columns[1].Id,
            Value = DocCellValue.Text("MarineElite"),
        });

        units.VariantDeltas.Add(variantDelta);
        return project;
    }

    private static DocProject BuildFormulaErrorProject()
    {
        var project = new DocProject { Name = "FormulaErr" };
        var table = new DocTable
        {
            Name = "Metrics",
            FileName = "metrics",
            ExportConfig = new DocTableExportConfig { Enabled = true },
        };

        var idColumn = new DocColumn { Name = "Id", Kind = DocColumnKind.Number, ExportType = "int" };
        var badColumn = new DocColumn { Name = "Bad", Kind = DocColumnKind.Number, ExportType = "int", FormulaExpression = "\"oops\"" };
        table.Columns.Add(idColumn);
        table.Columns.Add(badColumn);
        table.Keys.PrimaryKeyColumnId = idColumn.Id;

        var row = new DocRow { Id = "m0" };
        row.SetCell(idColumn.Id, DocCellValue.Number(0));
        table.Rows.Add(row);

        project.Tables.Add(table);
        return project;
    }

    private static DocProject BuildDerivedMultiMatchProject()
    {
        var project = new DocProject { Name = "DerivedMultiMatch" };

        var items = new DocTable { Name = "Items", FileName = "items" };
        var itemId = new DocColumn { Name = "Id", Kind = DocColumnKind.Number, ExportType = "int" };
        var itemKey = new DocColumn { Name = "Key", Kind = DocColumnKind.Text };
        items.Columns.Add(itemId);
        items.Columns.Add(itemKey);
        var itemRow = new DocRow { Id = "i0" };
        itemRow.SetCell(itemId.Id, DocCellValue.Number(0));
        itemRow.SetCell(itemKey.Id, DocCellValue.Text("A"));
        items.Rows.Add(itemRow);

        var stats = new DocTable { Name = "Stats", FileName = "stats" };
        var statKey = new DocColumn { Name = "Key", Kind = DocColumnKind.Text };
        var statValue = new DocColumn { Name = "Value", Kind = DocColumnKind.Number };
        stats.Columns.Add(statKey);
        stats.Columns.Add(statValue);
        stats.Rows.Add(MakeStatsRow("s0", statKey, "A", statValue, 1));
        stats.Rows.Add(MakeStatsRow("s1", statKey, "A", statValue, 2)); // duplicate join key -> MultiMatch

        var derived = new DocTable
        {
            Name = "InventoryView",
            FileName = "inventoryview",
            ExportConfig = new DocTableExportConfig { Enabled = true },
        };

        var outId = new DocColumn { Id = "out_id", Name = "Id", Kind = DocColumnKind.Number, ExportType = "int", IsProjected = true };
        var outKey = new DocColumn { Id = "out_key", Name = "Key", Kind = DocColumnKind.Text, IsProjected = true };
        var outValue = new DocColumn { Id = "out_value", Name = "Value", Kind = DocColumnKind.Number, IsProjected = true };
        derived.Columns.Add(outId);
        derived.Columns.Add(outKey);
        derived.Columns.Add(outValue);
        derived.Keys.PrimaryKeyColumnId = outId.Id;

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
                    KeyMappings =
                    {
                        new DerivedKeyMapping { BaseColumnId = outKey.Id, SourceColumnId = statKey.Id }
                    }
                }
            },
            Projections =
            {
                new DerivedProjection { SourceTableId = items.Id, SourceColumnId = itemId.Id, OutputColumnId = outId.Id },
                new DerivedProjection { SourceTableId = items.Id, SourceColumnId = itemKey.Id, OutputColumnId = outKey.Id },
                new DerivedProjection { SourceTableId = stats.Id, SourceColumnId = statValue.Id, OutputColumnId = outValue.Id },
            }
        };

        project.Tables.Add(items);
        project.Tables.Add(stats);
        project.Tables.Add(derived);
        return project;
    }

    private static DocRow MakeStatsRow(string id, DocColumn keyCol, string key, DocColumn valueCol, double value)
    {
        var row = new DocRow { Id = id };
        row.SetCell(keyCol.Id, DocCellValue.Text(key));
        row.SetCell(valueCol.Id, DocCellValue.Number(value));
        return row;
    }

    private static DocRow CloneRowWithId(DocRow row, string newId)
    {
        var clone = new DocRow { Id = newId };
        foreach (var kvp in row.Cells)
        {
            clone.Cells[kvp.Key] = kvp.Value;
        }
        return clone;
    }

    private static string RenderHarnessSource()
    {
        return """
        using System.Collections.Generic;
        using DerpDoc.Runtime;

        namespace DerpDocDatabase;

        public static class ExportTestHarness
        {
            private static UnitsTable LoadUnits(BinaryLoader loader)
            {
                var acc = loader.GetTableAccessor<Units>("Units");
                var pk = loader.GetTableAccessor<DerpDocKeyRowIndexPair>("Units__pk_sorted");
                var mapCode = loader.GetTableAccessor<int>("Units__sk_Code__unique");
                var pairsFaction = loader.GetTableAccessor<DerpDocKeyRowIndexPair>("Units__sk_Faction__pairs");
                return new UnitsTable(acc, pk, mapCode, pairsFaction);
            }

            public static string GetUnitName(string binPath, int id)
            {
                using var loader = BinaryLoader.Load(binPath);
                var units = LoadUnits(loader);
                if (!units.TryFindById(id, out var unit))
                {
                    return "";
                }
                return unit.Name.ToString();
            }

            public static int[] GetUnitIdsByFaction(string binPath, int factionKey)
            {
                using var loader = BinaryLoader.Load(binPath);
                var units = LoadUnits(loader);
                var ids = new List<int>();
                foreach (ref readonly var unit in units.FindByFaction(factionKey))
                {
                    ids.Add(unit.Id);
                }
                return ids.ToArray();
            }

            public static int[] GetUnitIdsByIdRange(string binPath, int min, int max)
            {
                using var loader = BinaryLoader.Load(binPath);
                var units = LoadUnits(loader);
                var ids = new List<int>();
                foreach (ref readonly var unit in units.FindRangeById(min, max))
                {
                    ids.Add(unit.Id);
                }
                return ids.ToArray();
            }
        }
        """;
    }

    private static string RenderSubtableHarnessSource()
    {
        return """
        using System.Collections.Generic;
        using DerpDoc.Runtime;

        namespace DerpDocDatabase;

        public static class SubtableHarness
        {
            private static LevelsTable LoadLevels(BinaryLoader loader)
            {
                var levels = new LevelsTable(
                    loader.GetTableAccessor<Levels>("Levels"),
                    loader.GetTableAccessor<DerpDocKeyRowIndexPair>("Levels__pk_sorted"));
                var items = new LevelsItemsTable(
                    loader.GetTableAccessor<LevelsItems>("LevelsItems"),
                    loader.GetTableAccessor<DerpDocKeyRowIndexPair>("LevelsItems__pk_sorted"),
                    loader.GetTableAccessor<DerpDocRangeStartCountPair>("LevelsItems__sub_parent_ranges"),
                    loader.GetTableAccessor<int>("LevelsItems__sub_parent_rows"));
                levels.ConnectSubtables(items);
                return levels;
            }

            public static int[] ReadChildValues(string binPath, int parentId)
            {
                using var loader = BinaryLoader.Load(binPath);
                var levels = LoadLevels(loader);
                var scope = levels.FindByIdView(parentId).Items;
                var values = new List<int>();
                foreach (ref readonly var row in scope.All)
                {
                    values.Add(row.Value);
                }
                return values.ToArray();
            }

            public static int ResolveParentId(string binPath, int parentId)
            {
                using var loader = BinaryLoader.Load(binPath);
                var levels = LoadLevels(loader);
                var scope = levels.FindByIdView(parentId).Items;
                return scope.FindById(0).Parent.Id;
            }
        }
        """;
    }

    private static string RenderRowRefHarnessSource()
    {
        return """
        using DerpDoc.Runtime;

        namespace DerpDocDatabase;

        public static class RowRefHarness
        {
            private static LevelsTable LoadLevels(BinaryLoader loader)
            {
                var levels = new LevelsTable(
                    loader.GetTableAccessor<Levels>("Levels"),
                    loader.GetTableAccessor<DerpDocKeyRowIndexPair>("Levels__pk_sorted"));
                var entities = new LevelsEntitiesTable(
                    loader.GetTableAccessor<LevelsEntities>("LevelsEntities"),
                    loader.GetTableAccessor<DerpDocKeyRowIndexPair>("LevelsEntities__pk_sorted"),
                    loader.GetTableAccessor<DerpDocRangeStartCountPair>("LevelsEntities__sub_parent_ranges"),
                    loader.GetTableAccessor<int>("LevelsEntities__sub_parent_rows"),
                    loader.GetTableAccessor<DerpDocTagPkPair>("LevelsEntities__rowref_entity_row_targets"),
                    loader.GetTableAccessor<DerpDocRangeStartCountPair>("LevelsEntities__rowref_entity_parent_kind_ranges"),
                    loader.GetTableAccessor<int>("LevelsEntities__rowref_entity_parent_kind_rows"),
                    loader.GetTableAccessor<DerpDocRangeStartCountPair>("LevelsEntities__rowref_entity_parent_kind_target_meta"),
                    loader.GetTableAccessor<DerpDocRangeStartCountPair>("LevelsEntities__rowref_entity_parent_kind_target_ranges"),
                    loader.GetTableAccessor<int>("LevelsEntities__rowref_entity_parent_kind_target_rows"),
                    loader.GetTableAccessor<DerpDocRangeStartCountPair>("LevelsEntities__rowref_entity_parent_target_meta"),
                    loader.GetTableAccessor<DerpDocRangeStartCountPair>("LevelsEntities__rowref_entity_parent_target_ranges"),
                    loader.GetTableAccessor<int>("LevelsEntities__rowref_entity_parent_target_rows"));
                levels.ConnectSubtables(entities);
                return levels;
            }

            public static int[] Evaluate(string binPath)
            {
                using var loader = BinaryLoader.Load(binPath);
                var levels = LoadLevels(loader);
                var entities = levels.FindByIdView(10).Entities;

                int globalEnemyMatches = entities.FindById(7).Count;
                int typedEnemyMatches = entities.Enemies.FindById(7).Count;
                int playerMatches = entities.Player.FindById(1).Count;
                int triggerMatches = entities.Triggers.FindById(50).Count;
                int parentId = entities.Enemies.FindPlacementById(100).Parent.Id;

                return new[] { globalEnemyMatches, typedEnemyMatches, playerMatches, triggerMatches, parentId };
            }
        }
        """;
    }

    private static string RenderRuntimeBindingHarnessSource()
    {
        return """
        using DerpDoc.Runtime;

        namespace DerpDocDatabase;

        public static class RuntimeBindingHarness
        {
            public static double[] Evaluate(string binPath)
            {
                using var loader = BinaryLoader.Load(binPath);

                var settingsTable = new SettingsTable(
                    loader.GetTableAccessor<Settings>("Settings"),
                    loader.GetTableAccessor<DerpDocKeyRowIndexPair>("Settings__pk_sorted"));
                var peopleTable = new PeopleTable(
                    loader.GetTableAccessor<People>("People"),
                    loader.GetTableAccessor<DerpDocKeyRowIndexPair>("People__pk_sorted"));

                var settingsRuntime = new SettingsRuntime(settingsTable);
                var peopleRuntime = new PeopleRuntime(peopleTable);

                settingsRuntime.ConnectRuntimes(peopleRuntime);
                peopleRuntime.ConnectRuntimes(settingsRuntime);

                var settings = settingsRuntime.CreateInstance();
                var peopleA = peopleRuntime.CreateInstance();
                var peopleB = peopleRuntime.CreateInstance();

                peopleA.Links.Settings = settings.Id;
                peopleA.Vars.Limit = 20d;
                peopleB.Vars.Limit = 3d;

                double linkedBefore = peopleA.Vars.LinkedLimit;
                settings.Vars.Limit = 9d;
                peopleA.Links.Settings = -1;
                peopleA.Links.Settings = settings.Id;
                double linkedAfter = peopleA.Vars.LinkedLimit;

                peopleA.Vars.FilterValue = "elite";
                peopleA.Vars.SortDesc = true;

                return new[]
                {
                    peopleA.Vars.Limit,
                    peopleB.Vars.Limit,
                    peopleA.Vars.EffectiveLimit,
                    peopleB.Vars.EffectiveLimit,
                    linkedBefore,
                    linkedAfter,
                    (double)peopleA.View.MainFilterValue_0.Length,
                    peopleA.View.MainSortDescending_0 ? 1d : 0d,
                    (double)peopleA.View.MainFilterColumn_0.Length,
                };
            }
        }
        """;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
