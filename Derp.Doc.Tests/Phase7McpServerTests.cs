using System.Text.Json;
using System.Text.Json.Nodes;
using Derp.Doc.Mcp;
using Derp.Doc.Model;
using Derp.Doc.Preferences;
using Derp.Doc.Plugins;
using Derp.Doc.Storage;
using Derp.Doc.Tables;
using DerpDoc.Runtime;

namespace Derp.Doc.Tests;

public sealed class Phase7McpServerTests
{
    private const string NanobananaDefaultApiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3-pro-image-preview";
    private const string NanobananaGeminiGenerateContentSuffix = ":generateContent";
    private const string ElevenLabsDefaultApiBaseUrl = "https://api.elevenlabs.io";
    private const string ElevenLabsGenerateEndpointPathPrefix = "/v1/text-to-speech/";
    private const string ElevenLabsEditEndpointPathPrefix = "/v1/speech-to-speech/";

    [Fact]
    public void Mcp_Initialize_ToolsList_And_BasicEditFlow_Work()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_mcp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
            {
                WorkspaceRoot = root,
                FollowUiActiveProject = false,
            });

            var initResponse = Send(server, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
            """);
            Assert.Equal("2.0", initResponse.RootElement.GetProperty("jsonrpc").GetString());
            Assert.Equal(1, initResponse.RootElement.GetProperty("id").GetInt32());
            Assert.True(initResponse.RootElement.TryGetProperty("result", out var initResult));
            Assert.Equal("2025-11-25", initResult.GetProperty("protocolVersion").GetString());
            string initInstructions = initResult.GetProperty("instructions").GetString() ?? "";
            Assert.Contains("Use Subtable when a parent row owns a variable-length list of child records", initInstructions, StringComparison.Ordinal);
            Assert.Contains("Use Relation when a field references reusable entities from another top-level table", initInstructions, StringComparison.Ordinal);

            SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

            var toolsList = Send(server, """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""");
            var tools = toolsList.RootElement.GetProperty("result").GetProperty("tools");
            Assert.Contains(tools.EnumerateArray(), t => t.GetProperty("name").GetString() == "derpdoc.project.open");
            Assert.Contains(tools.EnumerateArray(), t => t.GetProperty("name").GetString() == "derpdoc.row.add.batch");
            Assert.Contains(tools.EnumerateArray(), t => t.GetProperty("name").GetString() == "derpdoc.row.update.batch");
            Assert.Contains(tools.EnumerateArray(), t => t.GetProperty("name").GetString() == "derpdoc.row.delete.batch");
            Assert.Contains(tools.EnumerateArray(), t => t.GetProperty("name").GetString() == "derpdoc.table.query.batch");
            Assert.Contains(tools.EnumerateArray(), t => t.GetProperty("name").GetString() == "derpdoc.folder.create");
            Assert.Contains(tools.EnumerateArray(), t => t.GetProperty("name").GetString() == "derpdoc.document.create");
            Assert.Contains(tools.EnumerateArray(), t => t.GetProperty("name").GetString() == "derpdoc.block.add");
            Assert.Contains(tools.EnumerateArray(), t => t.GetProperty("name").GetString() == "derpdoc.variant.list");
            Assert.Contains(tools.EnumerateArray(), t => t.GetProperty("name").GetString() == "derpdoc.variant.set");
            Assert.Contains(tools.EnumerateArray(), t => t.GetProperty("name").GetString() == "derpdoc.variant.delete");
            Assert.Contains(tools.EnumerateArray(), t => t.GetProperty("name").GetString() == "derpdoc.project.legacy.variants.cleanup");
            Assert.Contains(tools.EnumerateArray(), t => t.GetProperty("name").GetString() == "derpdoc.table.schema.link.set");
            Assert.Contains(tools.EnumerateArray(), t => t.GetProperty("name").GetString() == "derpdoc.nodegraph.ensure");
            Assert.Contains(tools.EnumerateArray(), t => t.GetProperty("name").GetString() == "derpdoc.nodegraph.get");
            Assert.Contains(tools.EnumerateArray(), t => t.GetProperty("name").GetString() == "derpdoc.nodegraph.layout.set");
            Assert.Contains(tools.EnumerateArray(), t => t.GetProperty("name").GetString() == "derpdoc.nanobanana.generate");
            Assert.Contains(tools.EnumerateArray(), t => t.GetProperty("name").GetString() == "derpdoc.nanobanana.edit");
            Assert.Contains(tools.EnumerateArray(), t => t.GetProperty("name").GetString() == "derpdoc.elevenlabs.generate");
            Assert.Contains(tools.EnumerateArray(), t => t.GetProperty("name").GetString() == "derpdoc.elevenlabs.edit");

            var createProject = CallTool(server, 3, "derpdoc.project.create", new { path = "MyDb", name = "TestProject" });
            string dbRoot = createProject.GetProperty("dbRoot").GetString()!;
            Assert.True(Directory.Exists(dbRoot));
            Assert.True(File.Exists(Path.Combine(dbRoot, "project.json")));

            var openProject = CallTool(server, 4, "derpdoc.project.open", new { path = "MyDb" });
            Assert.Equal("TestProject", openProject.GetProperty("projectName").GetString());

            var createTable = CallTool(server, 5, "derpdoc.table.create", new { name = "Units", fileName = "units" });
            string unitsTableId = createTable.GetProperty("tableId").GetString()!;

            var addIdCol = CallTool(server, 6, "derpdoc.column.add", new { tableId = unitsTableId, name = "Id", kind = "Number" });
            string idColumnId = addIdCol.GetProperty("columnId").GetString()!;
            CallTool(server, 7, "derpdoc.column.update", new { tableId = unitsTableId, columnId = idColumnId, exportType = "int" });

            var addNameCol = CallTool(server, 8, "derpdoc.column.add", new { tableId = unitsTableId, name = "Name", kind = "Text" });
            string nameColumnId = addNameCol.GetProperty("columnId").GetString()!;

            CallTool(server, 9, "derpdoc.table.keys.set", new { tableId = unitsTableId, primaryKeyColumnId = idColumnId, secondaryKeys = Array.Empty<object>() });
            CallTool(server, 10, "derpdoc.table.export.set", new { tableId = unitsTableId, enabled = true });

            var addRow = CallTool(server, 11, "derpdoc.row.add", new { tableId = unitsTableId, cells = new Dictionary<string, object> { [idColumnId] = 0, [nameColumnId] = "Marine" } });
            string rowId = addRow.GetProperty("rowId").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(rowId));

            var query = CallTool(server, 12, "derpdoc.table.query", new { tableId = unitsTableId, limit = 10, offset = 0 });
            var rows = query.GetProperty("rows");
            Assert.Equal(1, rows.GetArrayLength());
            var rowCells = rows[0].GetProperty("cells");
            Assert.Equal("Marine", rowCells.GetProperty(nameColumnId).GetString());

            string outDir = Path.Combine(root, "Out");
            string binPath = Path.Combine(outDir, "Test.derpdoc");
            string genDir = Path.Combine(outDir, "Gen");

            var export = CallTool(server, 13, "derpdoc.export", new { path = "MyDb", generatedDir = genDir, binPath = binPath, noManifest = true });
            Assert.Equal(binPath, export.GetProperty("binPath").GetString());
            Assert.True(File.Exists(binPath));
            string livePath = export.GetProperty("livePath").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(livePath));
            Assert.True(File.Exists(livePath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Mcp_BatchRowUpdate_And_BatchTableQuery_Work()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_mcp_batch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
            {
                WorkspaceRoot = root,
                FollowUiActiveProject = false,
            });

            Send(server, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
            """);
            SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

            CallTool(server, 2, "derpdoc.project.create", new { path = "MyDbBatch", name = "BatchProject" });
            var createTable = CallTool(server, 3, "derpdoc.table.create", new { name = "Items", fileName = "items" });
            string tableId = createTable.GetProperty("tableId").GetString()!;

            var addNameColumn = CallTool(server, 4, "derpdoc.column.add", new { tableId, name = "Name", kind = "Text" });
            string nameColumnId = addNameColumn.GetProperty("columnId").GetString()!;
            var addPowerColumn = CallTool(server, 5, "derpdoc.column.add", new { tableId, name = "Power", kind = "Number" });
            string powerColumnId = addPowerColumn.GetProperty("columnId").GetString()!;

            var batchAdd = CallTool(server, 6, "derpdoc.row.add.batch", new
            {
                tableId,
                rows = new object[]
                {
                    new
                    {
                        rowId = "item-sword",
                        cells = new Dictionary<string, object>
                        {
                            [nameColumnId] = "Sword",
                            [powerColumnId] = 10,
                        }
                    },
                    new
                    {
                        rowId = "item-bow",
                        cells = new Dictionary<string, object>
                        {
                            [nameColumnId] = "Bow",
                            [powerColumnId] = 6,
                        }
                    },
                    new
                    {
                        rowId = "item-invalid",
                        cells = "not-an-object",
                    }
                }
            });
            Assert.Equal(2, batchAdd.GetProperty("addedCount").GetInt32());
            Assert.Equal(2, batchAdd.GetProperty("addedRowIds").GetArrayLength());
            Assert.Equal(1, batchAdd.GetProperty("errors").GetArrayLength());

            string rowOneId = batchAdd.GetProperty("addedRowIds")[0].GetString()!;
            string rowTwoId = batchAdd.GetProperty("addedRowIds")[1].GetString()!;

            var batchUpdate = CallTool(server, 7, "derpdoc.row.update.batch", new
            {
                tableId,
                updates = new object[]
                {
                    new
                    {
                        rowId = rowOneId,
                        cells = new Dictionary<string, object>
                        {
                            [powerColumnId] = 12,
                        }
                    },
                    new
                    {
                        rowId = rowTwoId,
                        cells = new Dictionary<string, object>
                        {
                            [powerColumnId] = 8,
                        }
                    },
                    new
                    {
                        rowId = "missing-row-id",
                        cells = new Dictionary<string, object>
                        {
                            [powerColumnId] = 1,
                        }
                    }
                }
            });
            Assert.Equal(2, batchUpdate.GetProperty("updatedCount").GetInt32());
            Assert.Equal(2, batchUpdate.GetProperty("updatedRowIds").GetArrayLength());
            Assert.Equal(1, batchUpdate.GetProperty("errors").GetArrayLength());

            var batchQuery = CallTool(server, 8, "derpdoc.table.query.batch", new
            {
                queries = new object[]
                {
                    new { tableId, limit = 10, offset = 0 },
                    new { tableId = "missing-table-id", limit = 10, offset = 0 },
                }
            });

            var results = batchQuery.GetProperty("results");
            Assert.Equal(2, results.GetArrayLength());
            Assert.Equal(tableId, results[0].GetProperty("tableId").GetString());
            Assert.Equal(2, results[0].GetProperty("rows").GetArrayLength());
            Assert.True(results[1].TryGetProperty("error", out var errorMessage));
            Assert.False(string.IsNullOrWhiteSpace(errorMessage.GetString()));

            var batchDelete = CallTool(server, 9, "derpdoc.row.delete.batch", new
            {
                tableId,
                rowIds = new object[]
                {
                    rowOneId,
                    rowTwoId,
                    "missing-row-id",
                    123,
                }
            });
            Assert.Equal(2, batchDelete.GetProperty("deletedCount").GetInt32());
            Assert.Equal(2, batchDelete.GetProperty("deletedRowIds").GetArrayLength());
            Assert.Equal(2, batchDelete.GetProperty("errors").GetArrayLength());

            var finalQuery = CallTool(server, 10, "derpdoc.table.query", new { tableId, limit = 10, offset = 0 });
            Assert.Equal(0, finalQuery.GetProperty("rows").GetArrayLength());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Mcp_Mutations_AutoWrite_LiveBinary_When_ExportEnabled()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_mcp_live_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
            {
                WorkspaceRoot = root,
                FollowUiActiveProject = false,
                AutoLiveExportOnMutation = true,
            });

            Send(server, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
            """);
            SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

            var createProject = CallTool(server, 2, "derpdoc.project.create", new { path = "MyDbLive", name = "LiveProject" });
            string dbRoot = createProject.GetProperty("dbRoot").GetString()!;
            Assert.True(Directory.Exists(dbRoot));

            var createTable = CallTool(server, 3, "derpdoc.table.create", new { name = "Weapons", fileName = "weapons" });
            string tableId = createTable.GetProperty("tableId").GetString()!;

            var addIdColumn = CallTool(server, 4, "derpdoc.column.add", new { tableId, name = "Id", kind = "Number" });
            string idColumnId = addIdColumn.GetProperty("columnId").GetString()!;
            CallTool(server, 5, "derpdoc.column.update", new { tableId, columnId = idColumnId, exportType = "int" });

            var addNameColumn = CallTool(server, 6, "derpdoc.column.add", new { tableId, name = "Name", kind = "Text" });
            string nameColumnId = addNameColumn.GetProperty("columnId").GetString()!;

            CallTool(server, 7, "derpdoc.table.keys.set", new
            {
                tableId,
                primaryKeyColumnId = idColumnId,
                secondaryKeys = Array.Empty<object>(),
            });
            CallTool(server, 8, "derpdoc.table.export.set", new { tableId, enabled = true });

            string livePath = Path.Combine(dbRoot, ".derpdoc-live.bin");
            Assert.True(File.Exists(livePath));

            uint generationBeforeAdd;
            using (var liveReader = LiveBinaryReader.Open(livePath))
            {
                generationBeforeAdd = liveReader.ReadHeader().Generation;
            }

            CallTool(server, 9, "derpdoc.row.add", new
            {
                tableId,
                cells = new Dictionary<string, object>
                {
                    [idColumnId] = 1,
                    [nameColumnId] = "Bronze Sword",
                }
            });

            using (var liveReader = LiveBinaryReader.Open(livePath))
            {
                uint generationAfterAdd = liveReader.ReadHeader().Generation;
                Assert.True(generationAfterAdd > generationBeforeAdd);
            }
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Mcp_ProjectLegacyVariantsCleanup_StripsProjectJsonVariants_And_PersistsTableVariants()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_mcp_legacy_variant_cleanup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
            {
                WorkspaceRoot = root,
                FollowUiActiveProject = false,
            });

            Send(server, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
            """);
            SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

            JsonElement createProject = CallTool(server, 2, "derpdoc.project.create", new { path = "MyDbLegacyVariants", name = "LegacyVariantsProject" });
            string dbRoot = createProject.GetProperty("dbRoot").GetString()!;
            string tableId = CallTool(server, 3, "derpdoc.table.create", new { name = "Units", fileName = "units" })
                .GetProperty("tableId").GetString()!;

            string projectJsonPath = Path.Combine(dbRoot, "project.json");
            string tableSchemaPath = Path.Combine(dbRoot, "tables", "units.schema.json");
            Assert.True(File.Exists(projectJsonPath));
            Assert.True(File.Exists(tableSchemaPath));

            JsonObject schemaBefore = JsonNode.Parse(File.ReadAllText(tableSchemaPath))?.AsObject()
                ?? throw new InvalidOperationException("Failed to parse table schema before cleanup.");
            Assert.False(schemaBefore.ContainsKey("variants"));

            JsonObject projectNode = JsonNode.Parse(File.ReadAllText(projectJsonPath))?.AsObject()
                ?? throw new InvalidOperationException("Failed to parse project.json before cleanup.");
            projectNode["variants"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = 1,
                    ["name"] = "Elite",
                }
            };
            File.WriteAllText(projectJsonPath, projectNode.ToJsonString());

            JsonElement cleanup = CallTool(server, 4, "derpdoc.project.legacy.variants.cleanup", new { });
            Assert.Equal(dbRoot, cleanup.GetProperty("dbRoot").GetString());
            Assert.True(cleanup.GetProperty("hadLegacyProjectVariants").GetBoolean());
            Assert.Equal(1, cleanup.GetProperty("legacyProjectVariantCount").GetInt32());
            Assert.Equal(0, cleanup.GetProperty("remainingLegacyProjectVariantCount").GetInt32());
            Assert.True(cleanup.GetProperty("cleanedProjectJson").GetBoolean());
            Assert.True(cleanup.GetProperty("saved").GetBoolean());

            JsonObject projectAfter = JsonNode.Parse(File.ReadAllText(projectJsonPath))?.AsObject()
                ?? throw new InvalidOperationException("Failed to parse project.json after cleanup.");
            Assert.False(projectAfter.ContainsKey("variants"));

            JsonObject schemaAfter = JsonNode.Parse(File.ReadAllText(tableSchemaPath))?.AsObject()
                ?? throw new InvalidOperationException("Failed to parse table schema after cleanup.");
            JsonArray? variantsAfter = schemaAfter["variants"]?.AsArray();
            Assert.NotNull(variantsAfter);
            Assert.Single(variantsAfter!);
            JsonObject variantNode = variantsAfter[0]?.AsObject()
                ?? throw new InvalidOperationException("Failed to parse migrated table variant.");
            Assert.Equal(1, variantNode["id"]?.GetValue<int>());
            Assert.Equal("Elite", variantNode["name"]?.GetValue<string>());

            JsonElement variantList = CallTool(server, 5, "derpdoc.variant.list", new { tableId });
            Assert.Equal(tableId, variantList.GetProperty("tableId").GetString());
            Assert.Contains(variantList.GetProperty("variants").EnumerateArray(), variant => variant.GetProperty("id").GetInt32() == 1);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Mcp_Requires_NotificationsInitialized_Before_ToolCalls()
    {
        var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
        {
            WorkspaceRoot = Path.GetTempPath(),
            FollowUiActiveProject = false,
        });

        Send(server, """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
        """);

        using (var beforeInitialized = Send(server, """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}"""))
        {
            var error = beforeInitialized.RootElement.GetProperty("error");
            Assert.Equal(-32000, error.GetProperty("code").GetInt32());
        }

        SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

        using (var afterInitialized = Send(server, """{"jsonrpc":"2.0","id":3,"method":"tools/list","params":{}}"""))
        {
            Assert.True(afterInitialized.RootElement.TryGetProperty("result", out _));
        }
    }

    [Fact]
    public void Mcp_ParseError_Is_JsonRpc_Error()
    {
        var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
        {
            FollowUiActiveProject = false,
        });

        Assert.True(server.TryHandleJsonRpc("{not json", out var response));
        Assert.False(string.IsNullOrWhiteSpace(response));

        using var doc = JsonDocument.Parse(response!);
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal(-32700, error.GetProperty("code").GetInt32());
    }

    [Fact]
    public void Mcp_UnknownMethod_Is_MethodNotFound()
    {
        var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
        {
            FollowUiActiveProject = false,
        });

        Send(server, """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
        """);
        SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

        using var response = Send(server, """{"jsonrpc":"2.0","id":2,"method":"derpdoc.nope","params":{}}""");
        var error = response.RootElement.GetProperty("error");
        Assert.Equal(-32601, error.GetProperty("code").GetInt32());
    }

    [Fact]
    public void Mcp_UnknownTool_Is_InvalidParams()
    {
        var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
        {
            FollowUiActiveProject = false,
        });

        Send(server, """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
        """);
        SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

        using var response = Send(server, """
        {"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"derpdoc.tool.does.not.exist","arguments":{}}}
        """);
        var error = response.RootElement.GetProperty("error");
        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
    }

    [Fact]
    public void Mcp_DocumentAndFolderMutationTools_Work()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_mcp_docs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
            {
                WorkspaceRoot = root,
                FollowUiActiveProject = false,
            });

            Send(server, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
            """);
            SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

            CallTool(server, 2, "derpdoc.project.create", new { path = "MyDbDocs", name = "DocsProject" });

            var tableFolderResult = CallTool(server, 3, "derpdoc.folder.create", new
            {
                name = "Gameplay",
                scope = "Tables",
            });
            string tableFolderId = tableFolderResult.GetProperty("folderId").GetString()!;

            var docsFolderResult = CallTool(server, 4, "derpdoc.folder.create", new
            {
                name = "Design",
                scope = "Documents",
            });
            string docsFolderId = docsFolderResult.GetProperty("folderId").GetString()!;

            var subDocsFolderResult = CallTool(server, 5, "derpdoc.folder.create", new
            {
                name = "Combat",
                scope = "Documents",
                parentFolderId = docsFolderId,
            });
            string subDocsFolderId = subDocsFolderResult.GetProperty("folderId").GetString()!;

            var docsFolders = CallTool(server, 6, "derpdoc.folder.list", new { scope = "Documents" }).GetProperty("folders");
            Assert.Equal(2, docsFolders.GetArrayLength());

            var createTable = CallTool(server, 7, "derpdoc.table.create", new { name = "EnemyTypes", fileName = "enemy_types" });
            string tableId = createTable.GetProperty("tableId").GetString()!;

            var setTableFolder = CallTool(server, 8, "derpdoc.table.folder.set", new
            {
                tableId,
                folderId = tableFolderId,
            });
            Assert.True(setTableFolder.GetProperty("updated").GetBoolean());
            Assert.Equal(tableFolderId, setTableFolder.GetProperty("folderId").GetString());

            var createDocument = CallTool(server, 9, "derpdoc.document.create", new
            {
                title = "Encounter Guide",
                folderId = subDocsFolderId,
                initialText = "Initial draft.",
            });
            string documentId = createDocument.GetProperty("documentId").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(documentId));

            var blockListBefore = CallTool(server, 10, "derpdoc.block.list", new
            {
                documentId,
                includeText = true,
            }).GetProperty("blocks");
            Assert.Single(blockListBefore.EnumerateArray());
            Assert.Equal("Initial draft.", blockListBefore[0].GetProperty("text").GetString());

            var addHeading = CallTool(server, 11, "derpdoc.block.add", new
            {
                documentId,
                index = 0,
                type = "Heading1",
                text = "Wave Breakdown",
            });
            string headingBlockId = addHeading.GetProperty("blockId").GetString()!;

            var addParagraph = CallTool(server, 12, "derpdoc.block.add", new
            {
                documentId,
                type = "Paragraph",
                text = "Use elite mix on wave 3.",
            });
            string paragraphBlockId = addParagraph.GetProperty("blockId").GetString()!;

            var updateParagraph = CallTool(server, 13, "derpdoc.block.update", new
            {
                documentId,
                blockId = paragraphBlockId,
                type = "CodeBlock",
                language = "json",
                text = "{\"waves\":3}",
            });
            Assert.True(updateParagraph.GetProperty("updated").GetBoolean());

            var moveHeading = CallTool(server, 14, "derpdoc.block.move", new
            {
                documentId,
                blockId = headingBlockId,
                index = 1,
            });
            Assert.True(moveHeading.GetProperty("updated").GetBoolean());
            Assert.Equal(1, moveHeading.GetProperty("index").GetInt32());

            var blockListAfterMove = CallTool(server, 15, "derpdoc.block.list", new
            {
                documentId,
                includeText = true,
            }).GetProperty("blocks");
            Assert.Equal(3, blockListAfterMove.GetArrayLength());

            string introBlockId = "";
            for (int blockIndex = 0; blockIndex < blockListAfterMove.GetArrayLength(); blockIndex++)
            {
                var block = blockListAfterMove[blockIndex];
                string? text = block.GetProperty("text").GetString();
                if (string.Equals(text, "Initial draft.", StringComparison.Ordinal))
                {
                    introBlockId = block.GetProperty("id").GetString() ?? "";
                    break;
                }
            }

            Assert.False(string.IsNullOrWhiteSpace(introBlockId));

            var deleteBlock = CallTool(server, 16, "derpdoc.block.delete", new
            {
                documentId,
                blockId = introBlockId,
            });
            Assert.True(deleteBlock.GetProperty("deleted").GetBoolean());

            var clearDocumentFolder = CallTool(server, 17, "derpdoc.document.folder.set", new
            {
                documentId,
                folderId = "",
            });
            Assert.True(clearDocumentFolder.GetProperty("updated").GetBoolean());
            Assert.Equal("", clearDocumentFolder.GetProperty("folderId").GetString());

            var updateDocument = CallTool(server, 18, "derpdoc.document.update", new
            {
                documentId,
                title = "Encounter Guide v2",
                folderId = docsFolderId,
            });
            Assert.True(updateDocument.GetProperty("updated").GetBoolean());

            var documents = CallTool(server, 19, "derpdoc.document.list", new { }).GetProperty("documents");
            Assert.Single(documents.EnumerateArray());
            Assert.Equal("Encounter Guide v2", documents[0].GetProperty("title").GetString());
            Assert.Equal(docsFolderId, documents[0].GetProperty("folderId").GetString());

            var deleteDocument = CallTool(server, 20, "derpdoc.document.delete", new { documentId });
            Assert.True(deleteDocument.GetProperty("deleted").GetBoolean());

            var documentsAfterDelete = CallTool(server, 21, "derpdoc.document.list", new { }).GetProperty("documents");
            Assert.Equal(0, documentsAfterDelete.GetArrayLength());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Mcp_BlockViewSet_RejectsUnknownViewId()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_mcp_block_view_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
            {
                WorkspaceRoot = root,
                FollowUiActiveProject = false,
            });

            Send(server, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
            """);
            SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

            var createProject = CallTool(server, 2, "derpdoc.project.create", new { path = "MyDbBlockView", name = "BlockViewProject" });
            string dbRoot = createProject.GetProperty("dbRoot").GetString()!;

            var table = new DocTable
            {
                Id = "table-main",
                Name = "Main",
                FileName = "main",
            };
            table.Views.Add(new DocView
            {
                Id = "view-main",
                Name = "Grid view",
                Type = DocViewType.Grid,
            });

            var document = new DocDocument
            {
                Id = "doc-main",
                Title = "Main doc",
                FileName = "main_doc",
            };
            document.Blocks.Add(new DocBlock
            {
                Id = "block-main",
                Order = "a0",
                Type = DocBlockType.Table,
                TableId = table.Id,
            });

            var project = new DocProject { Name = "BlockViewProject" };
            project.Tables.Add(table);
            project.Documents.Add(document);
            ProjectSerializer.Save(project, dbRoot);

            CallTool(server, 3, "derpdoc.project.open", new { path = "MyDbBlockView" });

            var error = CallToolExpectError(server, 4, "derpdoc.block.view.set", new
            {
                documentId = document.Id,
                blockId = "block-main",
                viewId = "missing-view-id",
            });

            string errorText = error.GetProperty("error").GetString() ?? "";
            Assert.Contains("not found", errorText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Mcp_ToolsList_ColumnSchemas_Allow_Null_For_Resettable_Fields()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_mcp_schema_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
            {
                WorkspaceRoot = root,
                FollowUiActiveProject = false,
            });

            Send(server, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
            """);
            SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

            using var toolsList = Send(server, """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""");
            var tools = toolsList.RootElement.GetProperty("result").GetProperty("tools");
            var columnAddTool = GetToolByName(tools, "derpdoc.column.add");
            var columnUpdateTool = GetToolByName(tools, "derpdoc.column.update");

            var columnAddProperties = columnAddTool.GetProperty("inputSchema").GetProperty("properties");
            Assert.True(SchemaTypeContains(columnAddProperties.GetProperty("typeId"), "null"));
            Assert.True(SchemaTypeContains(columnAddProperties.GetProperty("relationDisplayColumnId"), "null"));

            var columnUpdateProperties = columnUpdateTool.GetProperty("inputSchema").GetProperty("properties");
            Assert.True(SchemaTypeContains(columnUpdateProperties.GetProperty("typeId"), "null"));
            Assert.True(SchemaTypeContains(columnUpdateProperties.GetProperty("relationTableId"), "null"));
            Assert.True(SchemaTypeContains(columnUpdateProperties.GetProperty("relationDisplayColumnId"), "null"));
            Assert.True(SchemaTypeContains(columnUpdateProperties.GetProperty("exportType"), "null"));
            Assert.True(SchemaTypeContains(columnUpdateProperties.GetProperty("exportEnumName"), "null"));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Mcp_ColumnUpdate_TypeId_Null_Resets_To_BuiltIn()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_mcp_typeid_reset_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
            {
                WorkspaceRoot = root,
                FollowUiActiveProject = false,
            });

            Send(server, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
            """);
            SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

            CallTool(server, 2, "derpdoc.project.create", new { path = "MyDbTypeReset", name = "TypeResetProject" });
            var createTable = CallTool(server, 3, "derpdoc.table.create", new { name = "Stats", fileName = "stats" });
            string tableId = createTable.GetProperty("tableId").GetString()!;

            var addColumn = CallTool(server, 4, "derpdoc.column.add", new
            {
                tableId,
                name = "Power",
                kind = "Number",
                typeId = "plugin.tests.power",
            });
            string columnId = addColumn.GetProperty("columnId").GetString()!;

            var schemaBefore = CallTool(server, 5, "derpdoc.table.schema.get", new { tableId });
            var columnBefore = GetColumnById(schemaBefore.GetProperty("table").GetProperty("columns"), columnId);
            Assert.Equal("plugin.tests.power", GetPropertyIgnoreCase(columnBefore, "columnTypeId").GetString());

            CallTool(server, 6, "derpdoc.column.update", new
            {
                tableId,
                columnId,
                typeId = (string?)null,
            });

            var schemaAfter = CallTool(server, 7, "derpdoc.table.schema.get", new { tableId });
            var columnAfter = GetColumnById(schemaAfter.GetProperty("table").GetProperty("columns"), columnId);
            Assert.Equal(DocColumnTypeIds.Number, GetPropertyIgnoreCase(columnAfter, "columnTypeId").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Mcp_ColumnAdd_Accepts_TextureAsset_MeshAsset_And_AudioAsset()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_mcp_assets_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
            {
                WorkspaceRoot = root,
                FollowUiActiveProject = false,
            });

            Send(server, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
            """);
            SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

            CallTool(server, 2, "derpdoc.project.create", new { path = "MyDbAssetKinds", name = "AssetKindsProject" });
            var createTable = CallTool(server, 3, "derpdoc.table.create", new { name = "Visuals", fileName = "visuals" });
            string tableId = createTable.GetProperty("tableId").GetString()!;

            var textureColumnResult = CallTool(server, 4, "derpdoc.column.add", new { tableId, name = "Texture", kind = "TextureAsset" });
            var meshColumnResult = CallTool(server, 5, "derpdoc.column.add", new { tableId, name = "Mesh", kind = "MeshAsset" });
            var audioColumnResult = CallTool(server, 6, "derpdoc.column.add", new { tableId, name = "Voice", kind = "AudioAsset" });
            string textureColumnId = textureColumnResult.GetProperty("columnId").GetString()!;
            string meshColumnId = meshColumnResult.GetProperty("columnId").GetString()!;
            string audioColumnId = audioColumnResult.GetProperty("columnId").GetString()!;

            Assert.False(string.IsNullOrWhiteSpace(textureColumnId));
            Assert.False(string.IsNullOrWhiteSpace(meshColumnId));
            Assert.False(string.IsNullOrWhiteSpace(audioColumnId));

            CallTool(server, 7, "derpdoc.row.add", new
            {
                tableId,
                cells = new Dictionary<string, object>
                {
                    [textureColumnId] = "Textures/hero.png",
                    [meshColumnId] = "Meshes/tree.glb",
                    [audioColumnId] = "Audio/hero_voice.mp3",
                }
            });

            var query = CallTool(server, 8, "derpdoc.table.query", new { tableId, limit = 10, offset = 0 });
            var rows = query.GetProperty("rows");
            Assert.Equal(1, rows.GetArrayLength());
            var cells = rows[0].GetProperty("cells");
            Assert.Equal("Textures/hero.png", cells.GetProperty(textureColumnId).GetString());
            Assert.Equal("Meshes/tree.glb", cells.GetProperty(meshColumnId).GetString());
            Assert.Equal("Audio/hero_voice.mp3", cells.GetProperty(audioColumnId).GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Mcp_VariantTools_And_VariantRowOverlay_Work()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_mcp_variants_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
            {
                WorkspaceRoot = root,
                FollowUiActiveProject = false,
            });

            Send(server, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
            """);
            SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

            CallTool(server, 2, "derpdoc.project.create", new { path = "MyDbVariants", name = "VariantsProject" });
            string tableId = CallTool(server, 3, "derpdoc.table.create", new { name = "Units", fileName = "units" })
                .GetProperty("tableId").GetString()!;
            string nameColumnId = CallTool(server, 4, "derpdoc.column.add", new { tableId, name = "Name", kind = "Text" })
                .GetProperty("columnId").GetString()!;

            CallTool(server, 5, "derpdoc.row.add", new
            {
                tableId,
                rowId = "unit_marine",
                cells = new Dictionary<string, object>
                {
                    [nameColumnId] = "Marine",
                }
            });

            var setVariant = CallTool(server, 6, "derpdoc.variant.set", new { tableId, variantId = 1, name = "Elite" });
            Assert.True(setVariant.GetProperty("updated").GetBoolean());

            CallTool(server, 7, "derpdoc.row.update", new
            {
                tableId,
                variantId = 1,
                rowId = "unit_marine",
                cells = new Dictionary<string, object>
                {
                    [nameColumnId] = "Marine Elite",
                }
            });

            CallTool(server, 8, "derpdoc.row.add", new
            {
                tableId,
                variantId = 1,
                rowId = "unit_reaper",
                cells = new Dictionary<string, object>
                {
                    [nameColumnId] = "Reaper",
                }
            });

            var baseQuery = CallTool(server, 9, "derpdoc.table.query", new { tableId, variantId = 0, limit = 10, offset = 0 });
            Assert.Equal(0, baseQuery.GetProperty("variantId").GetInt32());
            Assert.Equal(1, baseQuery.GetProperty("rows").GetArrayLength());
            Assert.Equal("Marine", baseQuery.GetProperty("rows")[0].GetProperty("cells").GetProperty(nameColumnId).GetString());

            var eliteQuery = CallTool(server, 10, "derpdoc.table.query", new { tableId, variantId = 1, limit = 10, offset = 0 });
            Assert.Equal(1, eliteQuery.GetProperty("variantId").GetInt32());
            Assert.Equal(2, eliteQuery.GetProperty("rows").GetArrayLength());

            bool foundMarineElite = false;
            bool foundReaper = false;
            foreach (JsonElement row in eliteQuery.GetProperty("rows").EnumerateArray())
            {
                string? rowValue = row.GetProperty("cells").GetProperty(nameColumnId).GetString();
                if (string.Equals(rowValue, "Marine Elite", StringComparison.Ordinal))
                {
                    foundMarineElite = true;
                }
                else if (string.Equals(rowValue, "Reaper", StringComparison.Ordinal))
                {
                    foundReaper = true;
                }
            }

            Assert.True(foundMarineElite);
            Assert.True(foundReaper);

            CallTool(server, 11, "derpdoc.row.delete", new
            {
                tableId,
                variantId = 1,
                rowId = "unit_marine",
            });

            var eliteQueryAfterDelete = CallTool(server, 12, "derpdoc.table.query", new { tableId, variantId = 1, limit = 10, offset = 0 });
            Assert.Equal(1, eliteQueryAfterDelete.GetProperty("variantId").GetInt32());
            Assert.Single(eliteQueryAfterDelete.GetProperty("rows").EnumerateArray());
            Assert.Equal("Reaper", eliteQueryAfterDelete.GetProperty("rows")[0].GetProperty("cells").GetProperty(nameColumnId).GetString());

            JsonElement variants = CallTool(server, 13, "derpdoc.variant.list", new { tableId }).GetProperty("variants");
            Assert.Contains(variants.EnumerateArray(), variant => variant.GetProperty("id").GetInt32() == 0);
            Assert.Contains(variants.EnumerateArray(), variant => variant.GetProperty("id").GetInt32() == 1);

            CallTool(server, 14, "derpdoc.variant.delete", new { tableId, variantId = 1 });
            JsonElement queryError = CallToolExpectError(server, 15, "derpdoc.table.query", new { tableId, variantId = 1, limit = 10, offset = 0 });
            Assert.Contains("not found", queryError.GetProperty("error").GetString() ?? "", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Mcp_SystemTables_EnforceLocks_And_Metadata()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_mcp_system_tables_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
            {
                WorkspaceRoot = root,
                FollowUiActiveProject = false,
            });

            Send(server, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
            """);
            SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

            CallTool(server, 2, "derpdoc.project.create", new { path = "MyDbSystemTables", name = "SystemTablesProject" });
            JsonElement tableList = CallTool(server, 3, "derpdoc.table.list", new { }).GetProperty("tables");

            JsonElement assetsTable = default;
            JsonElement packagesTable = default;
            JsonElement exportsTable = default;
            bool foundAssetsTable = false;
            bool foundPackagesTable = false;
            bool foundExportsTable = false;
            foreach (JsonElement table in tableList.EnumerateArray())
            {
                string systemKey = GetPropertyIgnoreCase(table, "systemKey").GetString() ?? "";
                if (string.Equals(systemKey, DocSystemTableKeys.Assets, StringComparison.Ordinal))
                {
                    assetsTable = table.Clone();
                    foundAssetsTable = true;
                }
                else if (string.Equals(systemKey, DocSystemTableKeys.Packages, StringComparison.Ordinal))
                {
                    packagesTable = table.Clone();
                    foundPackagesTable = true;
                }
                else if (string.Equals(systemKey, DocSystemTableKeys.Exports, StringComparison.Ordinal))
                {
                    exportsTable = table.Clone();
                    foundExportsTable = true;
                }
            }

            Assert.True(foundAssetsTable);
            Assert.True(foundPackagesTable);
            Assert.True(foundExportsTable);

            string assetsTableId = GetPropertyIgnoreCase(assetsTable, "id").GetString()!;
            string packagesTableId = GetPropertyIgnoreCase(packagesTable, "id").GetString()!;
            string exportsTableId = GetPropertyIgnoreCase(exportsTable, "id").GetString()!;
            Assert.True(GetPropertyIgnoreCase(assetsTable, "isSystemTable").GetBoolean());
            Assert.Equal(DocSystemTableKeys.Assets, GetPropertyIgnoreCase(assetsTable, "systemKey").GetString());
            Assert.True(GetPropertyIgnoreCase(assetsTable, "systemSchemaLocked").GetBoolean());
            Assert.True(GetPropertyIgnoreCase(assetsTable, "systemDataLocked").GetBoolean());

            JsonElement assetsSchema = CallTool(server, 4, "derpdoc.table.schema.get", new { tableId = assetsTableId }).GetProperty("table");
            Assert.True(GetPropertyIgnoreCase(assetsSchema, "isSystemTable").GetBoolean());
            Assert.Equal(DocSystemTableKeys.Assets, GetPropertyIgnoreCase(assetsSchema, "systemKey").GetString());
            Assert.True(GetPropertyIgnoreCase(assetsSchema, "systemSchemaLocked").GetBoolean());
            Assert.True(GetPropertyIgnoreCase(assetsSchema, "systemDataLocked").GetBoolean());

            JsonElement exportsSchema = CallTool(server, 5, "derpdoc.table.schema.get", new { tableId = exportsTableId }).GetProperty("table");
            JsonElement exportsColumns = exportsSchema.GetProperty("columns");
            JsonElement exportsPackageColumn = GetColumnById(exportsColumns, "package_id");
            JsonElement exportsAssetColumn = GetColumnById(exportsColumns, "asset_id");
            Assert.Equal(packagesTableId, GetPropertyIgnoreCase(exportsPackageColumn, "relationTableId").GetString());
            Assert.Equal(assetsTableId, GetPropertyIgnoreCase(exportsAssetColumn, "relationTableId").GetString());

            JsonElement addAssetRowError = CallToolExpectError(server, 6, "derpdoc.row.add", new
            {
                tableId = assetsTableId,
                cells = new Dictionary<string, object>(),
            });
            Assert.Contains("locked row data", addAssetRowError.GetProperty("error").GetString() ?? "", StringComparison.OrdinalIgnoreCase);

            JsonElement addPackageColumnError = CallToolExpectError(server, 7, "derpdoc.column.add", new
            {
                tableId = packagesTableId,
                name = "Illegal",
                kind = "Text",
            });
            Assert.Contains("locked schema", addPackageColumnError.GetProperty("error").GetString() ?? "", StringComparison.OrdinalIgnoreCase);

            JsonElement addPackageRow = CallTool(server, 8, "derpdoc.row.add", new
            {
                tableId = packagesTableId,
                cells = new Dictionary<string, object>(),
            });
            Assert.False(string.IsNullOrWhiteSpace(addPackageRow.GetProperty("rowId").GetString()));

            JsonElement packageQuery = CallTool(server, 9, "derpdoc.table.query", new
            {
                tableId = packagesTableId,
                limit = 10,
                offset = 0,
            });
            Assert.True(packageQuery.GetProperty("rows").GetArrayLength() >= 1);
            string createdPackageRowId = addPackageRow.GetProperty("rowId").GetString() ?? "";
            JsonElement createdPackageRow = packageQuery.GetProperty("rows")
                .EnumerateArray()
                .Single(row => string.Equals(row.GetProperty("id").GetString(), createdPackageRowId, StringComparison.Ordinal));
            Assert.True(createdPackageRow.GetProperty("cells").GetProperty("package_id").GetDouble() > 0);

            JsonElement setAssetsVariantError = CallToolExpectError(server, 10, "derpdoc.variant.set", new
            {
                tableId = assetsTableId,
                variantId = 1,
                name = "Desktop",
            });
            Assert.Contains("does not support variants", setAssetsVariantError.GetProperty("error").GetString() ?? "", StringComparison.OrdinalIgnoreCase);

            JsonElement setPackagesVariant = CallTool(server, 11, "derpdoc.variant.set", new
            {
                tableId = packagesTableId,
                variantId = 1,
                name = "Desktop",
            });
            Assert.True(setPackagesVariant.GetProperty("updated").GetBoolean());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Mcp_SchemaLinkedTools_And_ColumnGuards_Work()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_mcp_schema_linked_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
            {
                WorkspaceRoot = root,
                FollowUiActiveProject = false,
            });

            Send(server, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
            """);
            SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

            CallTool(server, 2, "derpdoc.project.create", new { path = "MyDbSchemaLinked", name = "SchemaLinkedProject" });
            string sourceTableId = CallTool(server, 3, "derpdoc.table.create", new { name = "UnitSchema", fileName = "unit_schema" })
                .GetProperty("tableId").GetString()!;
            CallTool(server, 4, "derpdoc.column.add", new { tableId = sourceTableId, name = "Name", kind = "Text" });

            string linkedTableId = CallTool(server, 5, "derpdoc.table.create", new
            {
                name = "UnitOverrides",
                fileName = "unit_overrides",
                schemaSourceTableId = sourceTableId,
            }).GetProperty("tableId").GetString()!;

            JsonElement linkedSchema = CallTool(server, 6, "derpdoc.table.schema.get", new { tableId = linkedTableId }).GetProperty("table");
            Assert.True(linkedSchema.GetProperty("isSchemaLinked").GetBoolean());
            Assert.Equal(sourceTableId, linkedSchema.GetProperty("schemaSourceTableId").GetString());
            Assert.Single(linkedSchema.GetProperty("columns").EnumerateArray());

            JsonElement addColumnError = CallToolExpectError(server, 7, "derpdoc.column.add", new
            {
                tableId = linkedTableId,
                name = "Illegal",
                kind = "Text",
            });
            Assert.Contains("schema-linked", addColumnError.GetProperty("error").GetString() ?? "", StringComparison.OrdinalIgnoreCase);

            CallTool(server, 8, "derpdoc.column.add", new { tableId = sourceTableId, name = "Power", kind = "Number" });
            JsonElement linkedSchemaAfterSourceUpdate = CallTool(server, 9, "derpdoc.table.schema.get", new { tableId = linkedTableId }).GetProperty("table");
            Assert.Equal(2, linkedSchemaAfterSourceUpdate.GetProperty("columns").GetArrayLength());

            string localTableId = CallTool(server, 10, "derpdoc.table.create", new { name = "Local", fileName = "local" })
                .GetProperty("tableId").GetString()!;
            JsonElement setLinkResult = CallTool(server, 11, "derpdoc.table.schema.link.set", new
            {
                tableId = localTableId,
                schemaSourceTableId = sourceTableId,
            });
            Assert.True(setLinkResult.GetProperty("isSchemaLinked").GetBoolean());
            Assert.Equal(sourceTableId, setLinkResult.GetProperty("schemaSourceTableId").GetString());

            JsonElement clearLinkResult = CallTool(server, 12, "derpdoc.table.schema.link.set", new
            {
                tableId = localTableId,
                schemaSourceTableId = (string?)null,
            });
            Assert.False(clearLinkResult.GetProperty("isSchemaLinked").GetBoolean());
            Assert.Equal("", clearLinkResult.GetProperty("schemaSourceTableId").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Mcp_TableInheritance_Tools_And_ColumnGuards_Work()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_mcp_inheritance_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
            {
                WorkspaceRoot = root,
                FollowUiActiveProject = false,
            });

            Send(server, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
            """);
            SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

            CallTool(server, 2, "derpdoc.project.create", new { path = "MyDbInheritance", name = "InheritanceProject" });
            string baseTableId = CallTool(server, 3, "derpdoc.table.create", new { name = "EntityBase", fileName = "entity_base" })
                .GetProperty("tableId").GetString()!;
            string inheritedIdColumnId = CallTool(server, 4, "derpdoc.column.add", new
            {
                tableId = baseTableId,
                name = "EntityId",
                kind = "Number",
            }).GetProperty("columnId").GetString()!;
            string inheritedNameColumnId = CallTool(server, 5, "derpdoc.column.add", new
            {
                tableId = baseTableId,
                name = "Name",
                kind = "Text",
            }).GetProperty("columnId").GetString()!;
            CallTool(server, 6, "derpdoc.table.keys.set", new
            {
                tableId = baseTableId,
                primaryKeyColumnId = inheritedIdColumnId,
                secondaryKeys = Array.Empty<object>(),
            });

            string childTableId = CallTool(server, 7, "derpdoc.table.create", new
            {
                name = "Enemy",
                fileName = "enemy",
            }).GetProperty("tableId").GetString()!;
            string localCollisionNameColumnId = CallTool(server, 8, "derpdoc.column.add", new
            {
                tableId = childTableId,
                name = "Name",
                kind = "Text",
            }).GetProperty("columnId").GetString()!;
            string localColumnId = CallTool(server, 9, "derpdoc.column.add", new
            {
                tableId = childTableId,
                name = "Damage",
                kind = "Number",
            }).GetProperty("columnId").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(localColumnId));
            string childRowId = CallTool(server, 10, "derpdoc.row.add", new
            {
                tableId = childTableId,
                cells = new Dictionary<string, object>
                {
                    [localCollisionNameColumnId] = "Goblin",
                    [localColumnId] = 7,
                },
            }).GetProperty("rowId").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(childRowId));

            CallTool(server, 11, "derpdoc.table.inheritance.set", new
            {
                tableId = childTableId,
                inheritanceSourceTableId = baseTableId,
            });
            JsonElement childSchema = CallTool(server, 12, "derpdoc.table.schema.get", new { tableId = childTableId }).GetProperty("table");
            Assert.True(childSchema.GetProperty("isInherited").GetBoolean());
            Assert.Equal(baseTableId, childSchema.GetProperty("inheritanceSourceTableId").GetString());
            JsonElement childColumns = childSchema.GetProperty("columns");
            JsonElement childKeys = GetPropertyIgnoreCase(childSchema, "keys");
            Assert.Equal(inheritedIdColumnId, GetPropertyIgnoreCase(childKeys, "primaryKeyColumnId").GetString());
            Assert.Equal(3, childColumns.GetArrayLength());
            int inheritedNameColumnCount = 0;
            foreach (JsonElement childColumn in childColumns.EnumerateArray())
            {
                if (string.Equals(GetPropertyIgnoreCase(childColumn, "id").GetString(), inheritedNameColumnId, StringComparison.Ordinal))
                {
                    inheritedNameColumnCount++;
                    Assert.True(GetPropertyIgnoreCase(childColumn, "isInherited").GetBoolean());
                }
            }
            Assert.Equal(1, inheritedNameColumnCount);

            JsonElement childQueryAfterInheritance = CallTool(server, 13, "derpdoc.table.query", new
            {
                tableId = childTableId,
                limit = 10,
                offset = 0,
            });
            JsonElement childRowCells = childQueryAfterInheritance.GetProperty("rows")[0].GetProperty("cells");
            Assert.Equal("Goblin", childRowCells.GetProperty(inheritedNameColumnId).GetString());
            Assert.Equal(7d, childRowCells.GetProperty(localColumnId).GetDouble());

            JsonElement inheritedUpdateError = CallToolExpectError(server, 14, "derpdoc.column.update", new
            {
                tableId = childTableId,
                columnId = inheritedNameColumnId,
                name = "IllegalRename",
            });
            Assert.Contains("inherited", inheritedUpdateError.GetProperty("error").GetString() ?? "", StringComparison.OrdinalIgnoreCase);

            JsonElement inheritedDeleteError = CallToolExpectError(server, 15, "derpdoc.column.delete", new
            {
                tableId = childTableId,
                columnId = inheritedNameColumnId,
            });
            Assert.Contains("inherited", inheritedDeleteError.GetProperty("error").GetString() ?? "", StringComparison.OrdinalIgnoreCase);

            string inheritedHealthColumnId = CallTool(server, 16, "derpdoc.column.add", new
            {
                tableId = baseTableId,
                name = "Health",
                kind = "Number",
            }).GetProperty("columnId").GetString()!;
            JsonElement childSchemaAfterBaseUpdate = CallTool(server, 17, "derpdoc.table.schema.get", new { tableId = childTableId }).GetProperty("table");
            JsonElement childColumnsAfterBaseUpdate = childSchemaAfterBaseUpdate.GetProperty("columns");
            Assert.Equal(4, childColumnsAfterBaseUpdate.GetArrayLength());
            Assert.Contains(childColumnsAfterBaseUpdate.EnumerateArray(), column =>
                string.Equals(GetPropertyIgnoreCase(column, "id").GetString(), inheritedHealthColumnId, StringComparison.Ordinal) &&
                GetPropertyIgnoreCase(column, "isInherited").GetBoolean());
            JsonElement childKeysAfterBaseUpdate = GetPropertyIgnoreCase(childSchemaAfterBaseUpdate, "keys");
            Assert.Equal(inheritedIdColumnId, GetPropertyIgnoreCase(childKeysAfterBaseUpdate, "primaryKeyColumnId").GetString());

            CallTool(server, 18, "derpdoc.table.export.set", new
            {
                tableId = childTableId,
                enabled = true,
            });
            JsonElement childSchemaAfterExport = CallTool(server, 19, "derpdoc.table.schema.get", new { tableId = childTableId }).GetProperty("table");
            JsonElement exportConfig = GetPropertyIgnoreCase(childSchemaAfterExport, "exportConfig");
            Assert.Equal(JsonValueKind.Object, exportConfig.ValueKind);
            Assert.True(GetPropertyIgnoreCase(exportConfig, "enabled").GetBoolean());

            JsonElement clearInheritanceResult = CallTool(server, 20, "derpdoc.table.inheritance.set", new
            {
                tableId = childTableId,
                inheritanceSourceTableId = (string?)null,
            });
            Assert.False(clearInheritanceResult.GetProperty("isInherited").GetBoolean());
            Assert.Equal("", clearInheritanceResult.GetProperty("inheritanceSourceTableId").GetString());

            CallTool(server, 21, "derpdoc.column.update", new
            {
                tableId = childTableId,
                columnId = inheritedNameColumnId,
                name = "NameOverride",
            });
            JsonElement clearedChildSchema = CallTool(server, 22, "derpdoc.table.schema.get", new { tableId = childTableId }).GetProperty("table");
            JsonElement renamedColumn = clearedChildSchema.GetProperty("columns").EnumerateArray()
                .First(column => string.Equals(GetPropertyIgnoreCase(column, "id").GetString(), inheritedNameColumnId, StringComparison.Ordinal));
            Assert.Equal("NameOverride", GetPropertyIgnoreCase(renamedColumn, "name").GetString());
            Assert.False(GetPropertyIgnoreCase(renamedColumn, "isInherited").GetBoolean());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Mcp_NodeGraphTools_Scaffold_And_LayoutUpdate_Work()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_mcp_nodegraph_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
            {
                WorkspaceRoot = root,
                FollowUiActiveProject = false,
            });

            Send(server, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
            """);
            SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

            CallTool(server, 2, "derpdoc.project.create", new { path = "MyDbNodeGraph", name = "NodeGraphProject" });
            string tableId = CallTool(server, 3, "derpdoc.table.create", new { name = "DialogNodes", fileName = "dialog_nodes" })
                .GetProperty("tableId").GetString()!;
            string bodyColumnId = CallTool(server, 4, "derpdoc.column.add", new { tableId, name = "Body", kind = "Text" })
                .GetProperty("columnId").GetString()!;

            JsonElement ensureResult = CallTool(server, 5, "derpdoc.nodegraph.ensure", new
            {
                tableId,
                viewName = "Dialogue Graph",
            });
            string viewId = ensureResult.GetProperty("viewId").GetString()!;
            JsonElement ensureSchema = ensureResult.GetProperty("schema");
            Assert.False(string.IsNullOrWhiteSpace(ensureSchema.GetProperty("typeColumnId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(ensureSchema.GetProperty("positionColumnId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(ensureSchema.GetProperty("executionOutputColumnId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(ensureSchema.GetProperty("edgeSubtableColumnId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(ensureSchema.GetProperty("edgeTableId").GetString()));

            JsonElement schemaGet = CallTool(server, 6, "derpdoc.table.schema.get", new { tableId }).GetProperty("table");
            JsonElement columns = schemaGet.GetProperty("columns");
            Assert.Contains(columns.EnumerateArray(), column =>
                string.Equals(GetPropertyIgnoreCase(column, "name").GetString(), "Type", StringComparison.Ordinal));
            Assert.Contains(columns.EnumerateArray(), column =>
                string.Equals(GetPropertyIgnoreCase(column, "name").GetString(), "Pos", StringComparison.Ordinal));
            Assert.Contains(columns.EnumerateArray(), column =>
                string.Equals(GetPropertyIgnoreCase(column, "name").GetString(), "ExecNext", StringComparison.Ordinal));
            Assert.Contains(columns.EnumerateArray(), column =>
                string.Equals(GetPropertyIgnoreCase(column, "name").GetString(), "Edges", StringComparison.Ordinal));

            JsonElement getBefore = CallTool(server, 7, "derpdoc.nodegraph.get", new { tableId, viewId });
            Assert.Equal("Dialogue Graph", getBefore.GetProperty("viewName").GetString());
            JsonElement initialLayouts = getBefore.GetProperty("settings").GetProperty("typeLayouts");
            Assert.True(initialLayouts.GetArrayLength() > 0);

            JsonElement layoutSet = CallTool(server, 8, "derpdoc.nodegraph.layout.set", new
            {
                tableId,
                viewId,
                typeName = "Dialogue",
                nodeWidth = 340,
                fields = new object[]
                {
                    new
                    {
                        columnId = bodyColumnId,
                        mode = "InputPin",
                    }
                }
            });
            Assert.True(layoutSet.GetProperty("updated").GetBoolean());
            Assert.Equal("Dialogue", layoutSet.GetProperty("typeName").GetString());
            Assert.Equal(340d, layoutSet.GetProperty("nodeWidth").GetDouble());

            JsonElement getAfter = CallTool(server, 9, "derpdoc.nodegraph.get", new { tableId, viewId });
            JsonElement layouts = getAfter.GetProperty("settings").GetProperty("typeLayouts");
            JsonElement dialogueLayout = default;
            bool foundDialogueLayout = false;
            foreach (JsonElement layout in layouts.EnumerateArray())
            {
                if (string.Equals(layout.GetProperty("typeName").GetString(), "Dialogue", StringComparison.Ordinal))
                {
                    dialogueLayout = layout;
                    foundDialogueLayout = true;
                    break;
                }
            }

            Assert.True(foundDialogueLayout);
            Assert.Equal(340d, dialogueLayout.GetProperty("nodeWidth").GetDouble());

            JsonElement dialogueFields = dialogueLayout.GetProperty("fields");
            JsonElement bodyField = default;
            bool foundBodyField = false;
            foreach (JsonElement field in dialogueFields.EnumerateArray())
            {
                if (string.Equals(field.GetProperty("columnId").GetString(), bodyColumnId, StringComparison.Ordinal))
                {
                    bodyField = field;
                    foundBodyField = true;
                    break;
                }
            }

            Assert.True(foundBodyField);
            Assert.Equal("InputPin", bodyField.GetProperty("mode").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Mcp_BlockTableVariantId_RoundTrips_And_Resets_OnVariantDelete()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_mcp_block_variant_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
            {
                WorkspaceRoot = root,
                FollowUiActiveProject = false,
            });

            Send(server, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
            """);
            SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

            CallTool(server, 2, "derpdoc.project.create", new { path = "MyDbBlockVariant", name = "BlockVariantProject" });
            string tableId = CallTool(server, 3, "derpdoc.table.create", new { name = "Units", fileName = "units" })
                .GetProperty("tableId").GetString()!;
            CallTool(server, 4, "derpdoc.variant.set", new { tableId, variantId = 2, name = "Hard" });
            string documentId = CallTool(server, 5, "derpdoc.document.create", new { title = "Design" })
                .GetProperty("documentId").GetString()!;

            JsonElement blockAdd = CallTool(server, 6, "derpdoc.block.add", new
            {
                documentId,
                type = "Table",
                tableId,
                tableVariantId = 2,
            });
            string blockId = blockAdd.GetProperty("blockId").GetString()!;
            Assert.Equal(2, blockAdd.GetProperty("tableVariantId").GetInt32());

            JsonElement blocksBeforeDelete = CallTool(server, 7, "derpdoc.block.list", new { documentId, includeText = true }).GetProperty("blocks");
            JsonElement tableBlock = default;
            bool foundTableBlock = false;
            foreach (JsonElement block in blocksBeforeDelete.EnumerateArray())
            {
                if (string.Equals(block.GetProperty("id").GetString(), blockId, StringComparison.Ordinal))
                {
                    tableBlock = block;
                    foundTableBlock = true;
                    break;
                }
            }

            Assert.True(foundTableBlock);
            Assert.Equal(2, tableBlock.GetProperty("tableVariantId").GetInt32());

            CallTool(server, 8, "derpdoc.variant.delete", new { tableId, variantId = 2 });
            JsonElement blocksAfterDelete = CallTool(server, 9, "derpdoc.block.list", new { documentId, includeText = true }).GetProperty("blocks");
            JsonElement tableBlockAfterDelete = default;
            bool foundTableBlockAfterDelete = false;
            foreach (JsonElement block in blocksAfterDelete.EnumerateArray())
            {
                if (string.Equals(block.GetProperty("id").GetString(), blockId, StringComparison.Ordinal))
                {
                    tableBlockAfterDelete = block;
                    foundTableBlockAfterDelete = true;
                    break;
                }
            }

            Assert.True(foundTableBlockAfterDelete);
            Assert.Equal(0, tableBlockAfterDelete.GetProperty("tableVariantId").GetInt32());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Mcp_NanobananaGenerate_SavesAsset_And_AssignsTextureCell()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_mcp_nanobanana_generate_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var fakeClient = new FakeNanobananaClient();
            byte[] generatedImageBytes = [1, 2, 3, 4, 5, 6];
            fakeClient.EnqueueResponse(generatedImageBytes, """{"imageBase64":"AQIDBAUG"}""");

            var userPreferences = new DocUserPreferences();
            userPreferences.SetPluginSetting("nanobanana.apiBaseUrl", "https://api.nanobanana.local");
            userPreferences.SetPluginSetting("nanobanana.apiKey", "test-key");

            var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
            {
                WorkspaceRoot = root,
                FollowUiActiveProject = false,
                NanobananaClient = fakeClient,
                UserPreferencesReader = () => userPreferences,
            });

            Send(server, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
            """);
            SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

            JsonElement createProject = CallTool(server, 2, "derpdoc.project.create", new { path = "MyDbNanoGenerate", name = "NanoGenerateProject" });
            string dbRoot = createProject.GetProperty("dbRoot").GetString()!;

            string tableId = CallTool(server, 3, "derpdoc.table.create", new { name = "Visuals", fileName = "visuals" })
                .GetProperty("tableId").GetString()!;
            string textureColumnId = CallTool(server, 4, "derpdoc.column.add", new { tableId, name = "Portrait", kind = "TextureAsset" })
                .GetProperty("columnId").GetString()!;
            string rowId = CallTool(server, 5, "derpdoc.row.add", new { tableId, rowId = "unit.hero", cells = new Dictionary<string, object>() })
                .GetProperty("rowId").GetString()!;

            JsonElement generateResult = CallTool(server, 6, "derpdoc.nanobanana.generate", new
            {
                request = new
                {
                    prompt = "Hero portrait close-up",
                    size = "1024x1024",
                },
                outputName = "hero_portrait",
                tableId,
                rowId,
                columnId = textureColumnId,
            });

            Assert.Equal("generate", generateResult.GetProperty("operation").GetString());
            Assert.Equal("Generated/Nanobanana/hero_portrait.png", generateResult.GetProperty("assetPath").GetString());
            Assert.True(generateResult.GetProperty("rowUpdated").GetBoolean());
            Assert.False(generateResult.GetProperty("overwroteExisting").GetBoolean());
            Assert.Equal(0, generateResult.GetProperty("variantId").GetInt32());
            Assert.Equal(NanobananaGeminiGenerateContentSuffix, fakeClient.EndpointPaths[0]);

            string outputFile = Path.Combine(dbRoot, "Assets", "Generated", "Nanobanana", "hero_portrait.png");
            Assert.True(File.Exists(outputFile));
            Assert.Equal(generatedImageBytes, File.ReadAllBytes(outputFile));

            JsonElement queryResult = CallTool(server, 7, "derpdoc.table.query", new { tableId, offset = 0, limit = 10 });
            JsonElement queriedRows = queryResult.GetProperty("rows");
            Assert.Single(queriedRows.EnumerateArray());
            string cellValue = queriedRows[0].GetProperty("cells").GetProperty(textureColumnId).GetString() ?? "";
            Assert.Equal("Generated/Nanobanana/hero_portrait.png", cellValue);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Mcp_NanobananaEdit_ExplicitOutputName_OverwritesExistingFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_mcp_nanobanana_edit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var fakeClient = new FakeNanobananaClient();
            byte[] firstImageBytes = [10, 11, 12];
            byte[] secondImageBytes = [21, 22, 23, 24];
            fakeClient.EnqueueResponse(firstImageBytes, """{"imageBase64":"CgsM"}""");
            fakeClient.EnqueueResponse(secondImageBytes, """{"imageBase64":"FRYXGA=="}""");

            var userPreferences = new DocUserPreferences();
            userPreferences.SetPluginSetting("nanobanana.apiBaseUrl", "https://api.nanobanana.local");
            userPreferences.SetPluginSetting("nanobanana.apiKey", "test-key");

            var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
            {
                WorkspaceRoot = root,
                FollowUiActiveProject = false,
                NanobananaClient = fakeClient,
                UserPreferencesReader = () => userPreferences,
            });

            Send(server, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
            """);
            SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

            JsonElement createProject = CallTool(server, 2, "derpdoc.project.create", new { path = "MyDbNanoEdit", name = "NanoEditProject" });
            string dbRoot = createProject.GetProperty("dbRoot").GetString()!;

            JsonElement firstEditResult = CallTool(server, 3, "derpdoc.nanobanana.edit", new
            {
                request = new
                {
                    prompt = "Edit the existing portrait",
                    imageBase64 = "AQIDBA==",
                },
                outputName = "edited_portrait",
            });

            JsonElement secondEditResult = CallTool(server, 4, "derpdoc.nanobanana.edit", new
            {
                request = new
                {
                    prompt = "Adjust lighting and contrast",
                    imageBase64 = "AQIDBA==",
                },
                outputName = "edited_portrait",
            });

            Assert.Equal("edit", firstEditResult.GetProperty("operation").GetString());
            Assert.False(firstEditResult.GetProperty("overwroteExisting").GetBoolean());
            Assert.Equal("edit", secondEditResult.GetProperty("operation").GetString());
            Assert.True(secondEditResult.GetProperty("overwroteExisting").GetBoolean());
            Assert.Equal(NanobananaGeminiGenerateContentSuffix, fakeClient.EndpointPaths[0]);
            Assert.Equal(NanobananaGeminiGenerateContentSuffix, fakeClient.EndpointPaths[1]);

            string outputFile = Path.Combine(dbRoot, "Assets", "Generated", "Nanobanana", "edited_portrait.png");
            Assert.True(File.Exists(outputFile));
            Assert.Equal(secondImageBytes, File.ReadAllBytes(outputFile));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Mcp_NanobananaGenerate_UsesDefaultBaseUrl_WhenPreferenceMissing()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_mcp_nanobanana_default_base_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var fakeClient = new FakeNanobananaClient();
            fakeClient.EnqueueResponse([7, 8, 9], """{"imageBase64":"BwgJ"}""");

            var userPreferences = new DocUserPreferences();
            userPreferences.SetPluginSetting("nanobanana.apiKey", "test-key");

            var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
            {
                WorkspaceRoot = root,
                FollowUiActiveProject = false,
                NanobananaClient = fakeClient,
                UserPreferencesReader = () => userPreferences,
            });

            Send(server, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
            """);
            SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");
            CallTool(server, 2, "derpdoc.project.create", new { path = "MyDbNanoDefaultBase", name = "NanoDefaultBaseProject" });

            CallTool(server, 3, "derpdoc.nanobanana.generate", new
            {
                request = new
                {
                    prompt = "Single icon",
                },
                outputName = "default_base_test",
            });

            Assert.Single(fakeClient.ApiBaseUrls);
            Assert.Equal(NanobananaDefaultApiBaseUrl, fakeClient.ApiBaseUrls[0]);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Mcp_ElevenLabsGenerate_SavesAsset_And_AssignsAudioAssetCell()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_mcp_elevenlabs_generate_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var fakeClient = new FakeElevenLabsClient();
            byte[] generatedAudioBytes = [11, 22, 33, 44];
            fakeClient.EnqueueResponse(generatedAudioBytes, "");

            var userPreferences = new DocUserPreferences();
            userPreferences.SetPluginSetting("elevenlabs.apiBaseUrl", "https://api.elevenlabs.local");
            userPreferences.SetPluginSetting("elevenlabs.apiKey", "test-eleven-key");

            var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
            {
                WorkspaceRoot = root,
                FollowUiActiveProject = false,
                ElevenLabsClient = fakeClient,
                UserPreferencesReader = () => userPreferences,
            });

            Send(server, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
            """);
            SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

            JsonElement createProject = CallTool(server, 2, "derpdoc.project.create", new { path = "MyDbElevenGenerate", name = "ElevenGenerateProject" });
            string dbRoot = createProject.GetProperty("dbRoot").GetString()!;

            string tableId = CallTool(server, 3, "derpdoc.table.create", new { name = "Dialog", fileName = "dialog" })
                .GetProperty("tableId").GetString()!;
            string audioColumnId = CallTool(server, 4, "derpdoc.column.add", new { tableId, name = "VoicePath", kind = "AudioAsset" })
                .GetProperty("columnId").GetString()!;
            string rowId = CallTool(server, 5, "derpdoc.row.add", new { tableId, rowId = "line.001", cells = new Dictionary<string, object>() })
                .GetProperty("rowId").GetString()!;

            JsonElement generateResult = CallTool(server, 6, "derpdoc.elevenlabs.generate", new
            {
                request = new
                {
                    voiceId = "voice_alpha",
                    text = "For honor and glory",
                    model_id = "eleven_multilingual_v2",
                },
                outputName = "line_001",
                tableId,
                rowId,
                columnId = audioColumnId,
            });

            Assert.Equal("generate", generateResult.GetProperty("operation").GetString());
            Assert.Equal("Generated/ElevenLabs/line_001.mp3", generateResult.GetProperty("assetPath").GetString());
            Assert.True(generateResult.GetProperty("rowUpdated").GetBoolean());
            Assert.False(generateResult.GetProperty("overwroteExisting").GetBoolean());
            Assert.Equal(0, generateResult.GetProperty("variantId").GetInt32());
            Assert.Equal(ElevenLabsGenerateEndpointPathPrefix + "voice_alpha", fakeClient.EndpointPaths[0]);

            string outputFile = Path.Combine(dbRoot, "Assets", "Generated", "ElevenLabs", "line_001.mp3");
            Assert.True(File.Exists(outputFile));
            Assert.Equal(generatedAudioBytes, File.ReadAllBytes(outputFile));

            JsonElement queryResult = CallTool(server, 7, "derpdoc.table.query", new { tableId, offset = 0, limit = 10 });
            JsonElement queriedRows = queryResult.GetProperty("rows");
            Assert.Single(queriedRows.EnumerateArray());
            string cellValue = queriedRows[0].GetProperty("cells").GetProperty(audioColumnId).GetString() ?? "";
            Assert.Equal("Generated/ElevenLabs/line_001.mp3", cellValue);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Mcp_ElevenLabsEdit_ExplicitOutputName_OverwritesExistingFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_mcp_elevenlabs_edit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var fakeClient = new FakeElevenLabsClient();
            byte[] firstAudioBytes = [1, 2, 3];
            byte[] secondAudioBytes = [4, 5, 6, 7];
            fakeClient.EnqueueResponse(firstAudioBytes, "");
            fakeClient.EnqueueResponse(secondAudioBytes, "");

            var userPreferences = new DocUserPreferences();
            userPreferences.SetPluginSetting("elevenlabs.apiBaseUrl", "https://api.elevenlabs.local");
            userPreferences.SetPluginSetting("elevenlabs.apiKey", "test-eleven-key");

            var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
            {
                WorkspaceRoot = root,
                FollowUiActiveProject = false,
                ElevenLabsClient = fakeClient,
                UserPreferencesReader = () => userPreferences,
            });

            Send(server, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
            """);
            SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

            JsonElement createProject = CallTool(server, 2, "derpdoc.project.create", new { path = "MyDbElevenEdit", name = "ElevenEditProject" });
            string dbRoot = createProject.GetProperty("dbRoot").GetString()!;

            JsonElement firstEditResult = CallTool(server, 3, "derpdoc.elevenlabs.edit", new
            {
                request = new
                {
                    voiceId = "voice_alpha",
                    audioBase64 = "AQIDBA==",
                    model_id = "eleven_english_sts_v2",
                },
                outputName = "line_001_edit",
            });

            JsonElement secondEditResult = CallTool(server, 4, "derpdoc.elevenlabs.edit", new
            {
                request = new
                {
                    voiceId = "voice_alpha",
                    audioBase64 = "AQIDBA==",
                    model_id = "eleven_english_sts_v2",
                },
                outputName = "line_001_edit",
            });

            Assert.Equal("edit", firstEditResult.GetProperty("operation").GetString());
            Assert.False(firstEditResult.GetProperty("overwroteExisting").GetBoolean());
            Assert.Equal("edit", secondEditResult.GetProperty("operation").GetString());
            Assert.True(secondEditResult.GetProperty("overwroteExisting").GetBoolean());
            Assert.Equal(ElevenLabsEditEndpointPathPrefix + "voice_alpha", fakeClient.EndpointPaths[0]);
            Assert.Equal(ElevenLabsEditEndpointPathPrefix + "voice_alpha", fakeClient.EndpointPaths[1]);
            Assert.Equal(4, fakeClient.InputAudioByteCounts[0]);
            Assert.Equal(4, fakeClient.InputAudioByteCounts[1]);

            string outputFile = Path.Combine(dbRoot, "Assets", "Generated", "ElevenLabs", "line_001_edit.mp3");
            Assert.True(File.Exists(outputFile));
            Assert.Equal(secondAudioBytes, File.ReadAllBytes(outputFile));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Mcp_ElevenLabsGenerate_UsesDefaultBaseUrl_WhenPreferenceMissing()
    {
        string root = Path.Combine(Path.GetTempPath(), "derpdoc_mcp_elevenlabs_default_base_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var fakeClient = new FakeElevenLabsClient();
            fakeClient.EnqueueResponse([8, 9, 10], "");

            var userPreferences = new DocUserPreferences();
            userPreferences.SetPluginSetting("elevenlabs.apiKey", "test-eleven-key");

            var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
            {
                WorkspaceRoot = root,
                FollowUiActiveProject = false,
                ElevenLabsClient = fakeClient,
                UserPreferencesReader = () => userPreferences,
            });

            Send(server, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}
            """);
            SendNotification(server, """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");
            CallTool(server, 2, "derpdoc.project.create", new { path = "MyDbElevenDefaultBase", name = "ElevenDefaultBaseProject" });

            CallTool(server, 3, "derpdoc.elevenlabs.generate", new
            {
                request = new
                {
                    voiceId = "voice_alpha",
                    text = "Default base URL test",
                },
                outputName = "default_base_test",
            });

            Assert.Single(fakeClient.ApiBaseUrls);
            Assert.Equal(ElevenLabsDefaultApiBaseUrl, fakeClient.ApiBaseUrls[0]);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static JsonDocument Send(DerpDocMcpServer server, string jsonLine)
    {
        Assert.True(server.TryHandleJsonRpc(jsonLine, out var response));
        Assert.False(string.IsNullOrWhiteSpace(response));
        return JsonDocument.Parse(response!);
    }

    private static void SendNotification(DerpDocMcpServer server, string jsonLine)
    {
        bool handled = server.TryHandleJsonRpc(jsonLine, out var response);
        Assert.False(handled);
        Assert.True(string.IsNullOrWhiteSpace(response));
    }

    private static JsonElement CallTool(DerpDocMcpServer server, int id, string name, object args)
    {
        string json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            method = "tools/call",
            @params = new
            {
                name,
                arguments = args,
            }
        });

        using var response = Send(server, json);
        var result = response.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.True(result.TryGetProperty("structuredContent", out var structured));
        return structured.Clone();
    }

    private static JsonElement CallToolExpectError(DerpDocMcpServer server, int id, string name, object args)
    {
        string json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            method = "tools/call",
            @params = new
            {
                name,
                arguments = args,
            }
        });

        using var response = Send(server, json);
        var result = response.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.True(result.TryGetProperty("structuredContent", out var structured));
        return structured.Clone();
    }

    private static JsonElement GetToolByName(JsonElement tools, string toolName)
    {
        foreach (var tool in tools.EnumerateArray())
        {
            if (string.Equals(tool.GetProperty("name").GetString(), toolName, StringComparison.Ordinal))
            {
                return tool.Clone();
            }
        }

        throw new InvalidOperationException($"Tool not found: {toolName}");
    }

    private static JsonElement GetColumnById(JsonElement columns, string columnId)
    {
        foreach (var column in columns.EnumerateArray())
        {
            if (string.Equals(GetPropertyIgnoreCase(column, "id").GetString(), columnId, StringComparison.Ordinal))
            {
                return column.Clone();
            }
        }

        throw new InvalidOperationException($"Column not found: {columnId}");
    }

    private static JsonElement GetPropertyIgnoreCase(JsonElement jsonObject, string propertyName)
    {
        if (jsonObject.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Expected object to read property '{propertyName}'.");
        }

        if (jsonObject.TryGetProperty(propertyName, out var exactProperty))
        {
            return exactProperty;
        }

        foreach (var prop in jsonObject.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return prop.Value;
            }
        }

        throw new InvalidOperationException($"Property not found: {propertyName}");
    }

    private static bool SchemaTypeContains(JsonElement propertySchema, string expectedType)
    {
        if (!propertySchema.TryGetProperty("type", out var typeElement))
        {
            return false;
        }

        if (typeElement.ValueKind == JsonValueKind.String)
        {
            return string.Equals(typeElement.GetString(), expectedType, StringComparison.Ordinal);
        }

        if (typeElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var typeValue in typeElement.EnumerateArray())
            {
                if (typeValue.ValueKind == JsonValueKind.String &&
                    string.Equals(typeValue.GetString(), expectedType, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
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

    private sealed class FakeNanobananaClient : IDerpDocNanobananaClient
    {
        private readonly Queue<(byte[] ImageBytes, string ResponseJson)> _queuedResponses = new();

        public List<string> ApiBaseUrls { get; } = new();
        public List<string> EndpointPaths { get; } = new();

        public void EnqueueResponse(byte[] imageBytes, string responseJson)
        {
            _queuedResponses.Enqueue((imageBytes, responseJson));
        }

        public bool TryInvoke(
            string apiBaseUrl,
            string endpointPath,
            string apiKey,
            string requestJson,
            out byte[] imageBytes,
            out string responseJson,
            out string errorMessage)
        {
            ApiBaseUrls.Add(apiBaseUrl);
            EndpointPaths.Add(endpointPath);

            if (_queuedResponses.Count <= 0)
            {
                imageBytes = Array.Empty<byte>();
                responseJson = "{}";
                errorMessage = "No queued fake nanobanana response.";
                return false;
            }

            (byte[] ImageBytes, string ResponseJson) queuedResponse = _queuedResponses.Dequeue();
            imageBytes = queuedResponse.ImageBytes;
            responseJson = queuedResponse.ResponseJson;
            errorMessage = "";
            return true;
        }
    }

    private sealed class FakeElevenLabsClient : IDerpDocElevenLabsClient
    {
        private readonly Queue<(byte[] AudioBytes, string ResponseText)> _queuedResponses = new();

        public List<string> ApiBaseUrls { get; } = new();
        public List<string> EndpointPaths { get; } = new();
        public List<int> InputAudioByteCounts { get; } = new();

        public void EnqueueResponse(byte[] audioBytes, string responseText)
        {
            _queuedResponses.Enqueue((audioBytes, responseText));
        }

        public bool TryTextToSpeech(
            string apiBaseUrl,
            string apiKey,
            string voiceId,
            string outputFormat,
            bool? enableLogging,
            string requestJson,
            out byte[] audioBytes,
            out string responseText,
            out string errorMessage)
        {
            ApiBaseUrls.Add(apiBaseUrl);
            EndpointPaths.Add(ElevenLabsGenerateEndpointPathPrefix + voiceId);
            InputAudioByteCounts.Add(0);
            return TryDequeueResponse(out audioBytes, out responseText, out errorMessage);
        }

        public bool TrySpeechToSpeech(
            string apiBaseUrl,
            string apiKey,
            string voiceId,
            string outputFormat,
            bool? enableLogging,
            string requestJson,
            byte[] inputAudioBytes,
            string inputAudioFileName,
            string inputAudioMimeType,
            out byte[] audioBytes,
            out string responseText,
            out string errorMessage)
        {
            ApiBaseUrls.Add(apiBaseUrl);
            EndpointPaths.Add(ElevenLabsEditEndpointPathPrefix + voiceId);
            InputAudioByteCounts.Add(inputAudioBytes.Length);
            return TryDequeueResponse(out audioBytes, out responseText, out errorMessage);
        }

        private bool TryDequeueResponse(
            out byte[] audioBytes,
            out string responseText,
            out string errorMessage)
        {
            if (_queuedResponses.Count <= 0)
            {
                audioBytes = Array.Empty<byte>();
                responseText = "";
                errorMessage = "No queued fake ElevenLabs response.";
                return false;
            }

            (byte[] AudioBytes, string ResponseText) queuedResponse = _queuedResponses.Dequeue();
            audioBytes = queuedResponse.AudioBytes;
            responseText = queuedResponse.ResponseText;
            errorMessage = "";
            return true;
        }
    }
}
