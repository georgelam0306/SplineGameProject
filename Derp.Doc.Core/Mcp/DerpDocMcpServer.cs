using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Derp.Doc.Export;
using Derp.Doc.Model;
using Derp.Doc.Preferences;
using Derp.Doc.Plugins;
using Derp.Doc.Storage;
using Derp.Doc.Tables;

namespace Derp.Doc.Mcp;

public sealed class DerpDocMcpServer
{
    private static readonly string[] SupportedProtocolVersions =
    [
        "2025-11-25",
        "2025-03-26",
        "2024-11-05",
    ];
    private const string NodeGraphRendererId = "builtin.node-graph";
    private const string NodeGraphSettingsNamespace = "renderer.node-graph";
    private const string NodeGraphDefaultTypeColumnName = "Type";
    private const string NodeGraphDefaultPositionColumnName = "Pos";
    private const string NodeGraphDefaultTitleColumnName = "Title";
    private const string NodeGraphDefaultExecutionNextColumnName = "ExecNext";
    private const string NodeGraphDefaultEdgesColumnName = "Edges";
    private const string NodeGraphDefaultFromNodeColumnName = "FromNode";
    private const string NodeGraphDefaultFromPinColumnName = "FromPinId";
    private const string NodeGraphDefaultToNodeColumnName = "ToNode";
    private const string NodeGraphDefaultToPinColumnName = "ToPinId";
    private const string NodeGraphDefaultParentRowColumnName = "_parentRowId";
    private const string NodeGraphDefaultTypeOption = "Default";
    private const float NodeGraphDefaultNodeWidth = 240f;
    private const float NodeGraphMinNodeWidth = 160f;
    private const float NodeGraphMaxNodeWidth = 520f;
    private const string NanobananaDefaultOutputFolder = "Generated/Nanobanana";
    private const string NanobananaDefaultApiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3-pro-image-preview";
    private const string NanobananaGenerateEndpointPath = ":generateContent";
    private const string NanobananaEditEndpointPath = ":generateContent";
    private const string NanobananaApiBaseUrlPreferenceKey = "nanobanana.apiBaseUrl";
    private const string NanobananaApiKeyPreferenceKey = "nanobanana.apiKey";
    private const string ElevenLabsDefaultOutputFolder = "Generated/ElevenLabs";
    private const string ElevenLabsDefaultApiBaseUrl = "https://api.elevenlabs.io";
    private const string ElevenLabsDefaultOutputFormat = "mp3_44100_128";
    private const string ElevenLabsApiBaseUrlPreferenceKey = "elevenlabs.apiBaseUrl";
    private const string ElevenLabsApiKeyPreferenceKey = "elevenlabs.apiKey";

    private readonly DerpDocMcpServerOptions _options;
    private readonly IDerpDocNanobananaClient _nanobananaClient;
    private readonly IDerpDocElevenLabsClient _elevenLabsClient;
    private readonly Func<DocUserPreferences> _userPreferencesReader;
    private string _protocolVersion = SupportedProtocolVersions[0];
    private bool _clientInitialized;
    private string _activeDbRoot = "";
    private bool _activeDbRootIsFromUi;

    public DerpDocMcpServer(DerpDocMcpServerOptions options)
    {
        _options = options ?? new DerpDocMcpServerOptions();
        _nanobananaClient = _options.NanobananaClient ?? new DerpDocNanobananaHttpClient();
        _elevenLabsClient = _options.ElevenLabsClient ?? new DerpDocElevenLabsHttpClient();
        _userPreferencesReader = _options.UserPreferencesReader ?? DocUserPreferencesFile.Read;
        if (string.IsNullOrWhiteSpace(_options.WorkspaceRoot))
        {
            _options.WorkspaceRoot = Directory.GetCurrentDirectory();
        }
    }

    public string ActiveDbRoot => _activeDbRoot;

    public async Task RunStdioAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await Console.In.ReadLineAsync(cancellationToken);
            if (line == null)
            {
                return;
            }

            if (TryHandleJsonRpc(line, out var response) && response != null)
            {
                await Console.Out.WriteLineAsync(response);
                await Console.Out.FlushAsync(cancellationToken);
            }
        }
    }

    public bool TryHandleJsonRpc(string jsonLine, out string? responseJson)
    {
        responseJson = null;

        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(jsonLine);
        }
        catch (JsonException ex)
        {
            responseJson = WriteError(idElement: null, -32700, "Parse error", new { ex.Message });
            return true;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                responseJson = WriteError(idElement: null, -32600, "Invalid Request", null);
                return true;
            }

            string method = GetStringOrEmpty(root, "method");
            JsonElement? idElement = root.TryGetProperty("id", out var idProp) ? idProp : null;

            bool isNotification = !idElement.HasValue || idElement.Value.ValueKind == JsonValueKind.Undefined || idElement.Value.ValueKind == JsonValueKind.Null;

            if (string.IsNullOrWhiteSpace(method))
            {
                responseJson = WriteError(idElement, -32600, "Invalid Request", null);
                return true;
            }

            if (string.Equals(method, "notifications/initialized", StringComparison.Ordinal))
            {
                _clientInitialized = true;
                return false;
            }

            if (string.Equals(method, "ping", StringComparison.Ordinal))
            {
                if (isNotification)
                {
                    return false;
                }
                responseJson = WriteResult(idElement, writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteEndObject();
                });
                return true;
            }

            if (string.Equals(method, "initialize", StringComparison.Ordinal))
            {
                if (isNotification)
                {
                    return false;
                }

                _clientInitialized = false;
                var initParams = root.TryGetProperty("params", out var p) ? p : default;
                var requested = GetStringOrEmpty(initParams, "protocolVersion");
                _protocolVersion = NegotiateProtocolVersion(requested);

                responseJson = WriteResult(idElement, writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteString("protocolVersion", _protocolVersion);
                    writer.WritePropertyName("capabilities");
                    writer.WriteStartObject();
                    writer.WritePropertyName("tools");
                    writer.WriteStartObject();
                    writer.WriteBoolean("listChanged", false);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                    writer.WritePropertyName("serverInfo");
                    writer.WriteStartObject();
                    writer.WriteString("name", "derpdoc");
                    writer.WriteString("title", "Derp.Doc");
                    writer.WriteString("version", typeof(DerpDocMcpServer).Assembly.GetName().Version?.ToString() ?? "0.0.0");
                    writer.WriteString("description", "Derp.Doc MCP server (tables/docs editor data).");
                    writer.WriteEndObject();
                    writer.WriteString("instructions", """
                        Call derpdoc.project.open first to set the active project.
                        For multi-row or multi-table work, prefer batch tools (derpdoc.row.add.batch, derpdoc.row.update.batch, derpdoc.row.delete.batch, derpdoc.table.query.batch).
                        Use single-row tools only for one-off edits.
                        Run derpdoc.export when you need generated outputs refreshed.
                        Use Subtable when a parent row owns a variable-length list of child records (unlocks, rewards, costs, requirements).
                        Use Relation when a field references reusable entities from another top-level table (ore, ingot, recipe, building, tier).
                        Prefer Subtable + Relation over free-text lists for gameplay data integrity.
                        For milestone-style systems, model one parent row per milestone key (for example RepLevel) and move unlock entries into a child subtable.
                        After converting from scalar text to subtable/relation, remove deprecated scalar columns to prevent schema drift.
                        For node graphs, use derpdoc.nodegraph.ensure to scaffold required schema and create a node graph view.
                        Use derpdoc.nodegraph.layout.set to change per-type node width and pin/setting display modes.
                        """);
                    writer.WriteEndObject();
                });
                return true;
            }

            if (!_clientInitialized)
            {
                if (isNotification)
                {
                    return false;
                }

                responseJson = WriteError(idElement, -32000, "Client not initialized (missing notifications/initialized).", null);
                return true;
            }

            if (string.Equals(method, "tools/list", StringComparison.Ordinal))
            {
                if (isNotification)
                {
                    return false;
                }

                responseJson = WriteResult(idElement, writer =>
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("tools");
                    writer.WriteStartArray();
                    var tools = DerpDocMcpTools.All;
                    for (int i = 0; i < tools.Count; i++)
                    {
                        tools[i].WriteTo(writer);
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                });
                return true;
            }

            if (string.Equals(method, "tools/call", StringComparison.Ordinal))
            {
                if (isNotification)
                {
                    return false;
                }

                if (!root.TryGetProperty("params", out var callParams) || callParams.ValueKind != JsonValueKind.Object)
                {
                    responseJson = WriteError(idElement, -32602, "Invalid params", null);
                    return true;
                }

                string toolName = GetStringOrEmpty(callParams, "name");
                if (string.IsNullOrWhiteSpace(toolName))
                {
                    responseJson = WriteError(idElement, -32602, "Invalid params: missing tool name", null);
                    return true;
                }

                JsonElement args = callParams.TryGetProperty("arguments", out var a) ? a : default;

                if (!TryHandleToolCall(toolName, args, out var toolResultJson, out bool isToolError))
                {
                    responseJson = WriteError(idElement, -32602, $"Unknown tool: {toolName}", null);
                    return true;
                }

                responseJson = WriteResult(idElement, writer =>
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("content");
                    writer.WriteStartArray();
                    writer.WriteStartObject();
                    writer.WriteString("type", "text");
                    writer.WriteString("text", toolResultJson);
                    writer.WriteEndObject();
                    writer.WriteEndArray();
                    writer.WriteBoolean("isError", isToolError);
                    if (!string.IsNullOrEmpty(toolResultJson) && TryParseJsonObject(toolResultJson, out var structured))
                    {
                        writer.WritePropertyName("structuredContent");
                        structured.RootElement.WriteTo(writer);
                    }
                    writer.WriteEndObject();
                });
                return true;
            }

            if (isNotification)
            {
                return false;
            }

            responseJson = WriteError(idElement, -32601, "Method not found", new { method });
            return true;
        }
    }

    private static bool TryParseJsonObject(string json, out JsonDocument doc)
    {
        doc = null!;
        try
        {
            var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                parsed.Dispose();
                return false;
            }
            doc = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string NegotiateProtocolVersion(string requested)
    {
        for (int i = 0; i < SupportedProtocolVersions.Length; i++)
        {
            if (string.Equals(SupportedProtocolVersions[i], requested, StringComparison.Ordinal))
            {
                return requested;
            }
        }

        return SupportedProtocolVersions[0];
    }

	    private bool TryHandleToolCall(string toolName, JsonElement args, out string resultJson, out bool isError)
	    {
        resultJson = "";
        isError = false;

	        switch (toolName)
	        {
            case "derpdoc.project.create":
                return ToolProjectCreate(args, out resultJson, out isError);
            case "derpdoc.project.open":
                return ToolProjectOpen(args, out resultJson, out isError);
	            case "derpdoc.project.get":
	                return ToolProjectGet(out resultJson, out isError);
            case "derpdoc.project.legacy.variants.cleanup":
                return ToolProjectLegacyVariantsCleanup(out resultJson, out isError);
	            case "derpdoc.variant.list":
	                return ToolVariantList(args, out resultJson, out isError);
	            case "derpdoc.variant.set":
	                return ToolVariantSet(args, out resultJson, out isError);
	            case "derpdoc.variant.delete":
	                return ToolVariantDelete(args, out resultJson, out isError);
            case "derpdoc.folder.list":
                return ToolFolderList(args, out resultJson, out isError);
            case "derpdoc.folder.create":
                return ToolFolderCreate(args, out resultJson, out isError);
            case "derpdoc.table.list":
                return ToolTableList(out resultJson, out isError);
            case "derpdoc.table.create":
                return ToolTableCreate(args, out resultJson, out isError);
            case "derpdoc.table.delete":
                return ToolTableDelete(args, out resultJson, out isError);
            case "derpdoc.table.folder.set":
                return ToolTableFolderSet(args, out resultJson, out isError);
            case "derpdoc.table.schema.get":
                return ToolTableSchemaGet(args, out resultJson, out isError);
            case "derpdoc.table.schema.link.set":
                return ToolTableSchemaLinkSet(args, out resultJson, out isError);
            case "derpdoc.table.inheritance.set":
                return ToolTableInheritanceSet(args, out resultJson, out isError);
            case "derpdoc.column.add":
                return ToolColumnAdd(args, out resultJson, out isError);
            case "derpdoc.column.update":
                return ToolColumnUpdate(args, out resultJson, out isError);
            case "derpdoc.column.delete":
                return ToolColumnDelete(args, out resultJson, out isError);
            case "derpdoc.table.export.set":
                return ToolTableExportSet(args, out resultJson, out isError);
            case "derpdoc.table.keys.set":
                return ToolTableKeysSet(args, out resultJson, out isError);
            case "derpdoc.row.add":
                return ToolRowAdd(args, out resultJson, out isError);
            case "derpdoc.row.add.batch":
                return ToolRowAddBatch(args, out resultJson, out isError);
            case "derpdoc.row.update":
                return ToolRowUpdate(args, out resultJson, out isError);
            case "derpdoc.row.update.batch":
                return ToolRowUpdateBatch(args, out resultJson, out isError);
            case "derpdoc.row.delete":
                return ToolRowDelete(args, out resultJson, out isError);
            case "derpdoc.row.delete.batch":
                return ToolRowDeleteBatch(args, out resultJson, out isError);
            case "derpdoc.table.query":
                return ToolTableQuery(args, out resultJson, out isError);
            case "derpdoc.table.query.batch":
                return ToolTableQueryBatch(args, out resultJson, out isError);
            case "derpdoc.export":
                return ToolExport(args, out resultJson, out isError);
            case "derpdoc.view.list":
                return ToolViewList(args, out resultJson, out isError);
            case "derpdoc.view.create":
                return ToolViewCreate(args, out resultJson, out isError);
            case "derpdoc.view.update":
                return ToolViewUpdate(args, out resultJson, out isError);
            case "derpdoc.view.delete":
                return ToolViewDelete(args, out resultJson, out isError);
            case "derpdoc.nodegraph.ensure":
                return ToolNodeGraphEnsure(args, out resultJson, out isError);
            case "derpdoc.nodegraph.get":
                return ToolNodeGraphGet(args, out resultJson, out isError);
            case "derpdoc.nodegraph.layout.set":
                return ToolNodeGraphLayoutSet(args, out resultJson, out isError);
            case "derpdoc.document.list":
                return ToolDocumentList(out resultJson, out isError);
            case "derpdoc.document.create":
                return ToolDocumentCreate(args, out resultJson, out isError);
            case "derpdoc.document.update":
                return ToolDocumentUpdate(args, out resultJson, out isError);
            case "derpdoc.document.delete":
                return ToolDocumentDelete(args, out resultJson, out isError);
            case "derpdoc.document.folder.set":
                return ToolDocumentFolderSet(args, out resultJson, out isError);
            case "derpdoc.block.list":
                return ToolBlockList(args, out resultJson, out isError);
            case "derpdoc.block.add":
                return ToolBlockAdd(args, out resultJson, out isError);
            case "derpdoc.block.update":
                return ToolBlockUpdate(args, out resultJson, out isError);
            case "derpdoc.block.delete":
                return ToolBlockDelete(args, out resultJson, out isError);
            case "derpdoc.block.move":
                return ToolBlockMove(args, out resultJson, out isError);
            case "derpdoc.block.view.set":
                return ToolBlockViewSet(args, out resultJson, out isError);
            case "derpdoc.block.view.create":
                return ToolBlockViewCreate(args, out resultJson, out isError);
            case "derpdoc.derived.get":
                return ToolDerivedGet(args, out resultJson, out isError);
            case "derpdoc.derived.set":
                return ToolDerivedSet(args, out resultJson, out isError);
            case "derpdoc.formula.validate":
                return ToolFormulaValidate(args, out resultJson, out isError);
            case "derpdoc.nanobanana.generate":
                return ToolNanobananaGenerate(args, out resultJson, out isError);
            case "derpdoc.nanobanana.edit":
                return ToolNanobananaEdit(args, out resultJson, out isError);
            case "derpdoc.elevenlabs.generate":
                return ToolElevenLabsGenerate(args, out resultJson, out isError);
            case "derpdoc.elevenlabs.edit":
                return ToolElevenLabsEdit(args, out resultJson, out isError);
            default:
                return false;
	    }
    }

    private bool ToolProjectCreate(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        string path = GetArgString(args, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'path'." });
            return true;
        }

        string projectName = GetArgString(args, "name");
        if (string.IsNullOrWhiteSpace(projectName))
        {
            projectName = "DerpDocProject";
        }

        string dbRoot = ResolvePathWithinWorkspace(path);
        dbRoot = DocProjectPaths.ResolveDbRootFromPath(dbRoot, allowCreate: true, out var gameRoot);
        string scaffoldName = string.IsNullOrWhiteSpace(projectName)
            ? (!string.IsNullOrWhiteSpace(gameRoot) ? new DirectoryInfo(gameRoot).Name : new DirectoryInfo(dbRoot).Name)
            : projectName;
        dbRoot = DocProjectScaffolder.EnsureDbRoot(dbRoot, scaffoldName);

        _activeDbRoot = dbRoot;
        _activeDbRootIsFromUi = false;
        resultJson = JsonSerializer.Serialize(new { dbRoot });
        return true;
    }

    private bool ToolProjectOpen(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        string path = GetArgString(args, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'path'." });
            return true;
        }

        string fullPath = ResolvePathWithinWorkspace(path);
        bool createIfMissing = GetArgBool(args, "createIfMissing", true);
        string dbRoot = DocProjectPaths.ResolveDbRootFromPath(fullPath, allowCreate: createIfMissing, out var gameRoot);
        if (createIfMissing)
        {
            string projectName = new DirectoryInfo(!string.IsNullOrWhiteSpace(gameRoot) ? gameRoot : dbRoot).Name;
            dbRoot = DocProjectScaffolder.EnsureDbRoot(dbRoot, projectName);
        }

        var project = ProjectLoader.Load(dbRoot);
        _activeDbRoot = dbRoot;
        _activeDbRootIsFromUi = false;

        resultJson = JsonSerializer.Serialize(new { dbRoot, projectName = project.Name });
        return true;
    }

    private bool ToolProjectGet(out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out DocProject project, out resultJson))
        {
            isError = true;
            return true;
        }

        resultJson = JsonSerializer.Serialize(new
        {
            dbRoot = _activeDbRoot,
            projectName = project.Name,
            tableCount = project.Tables.Count,
        });
        return true;
    }

    private bool ToolProjectLegacyVariantsCleanup(out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        TrySyncActiveDbRootFromUi();
        if (string.IsNullOrWhiteSpace(_activeDbRoot))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "No active project. Call derpdoc.project.open first." });
            return true;
        }

        string projectJsonPath = Path.Combine(_activeDbRoot, "project.json");
        if (!TryReadLegacyProjectVariantInfo(projectJsonPath, out bool hasLegacyProjectVariants, out int legacyProjectVariantCount, out string legacyReadError))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = legacyReadError });
            return true;
        }

        if (!hasLegacyProjectVariants)
        {
            resultJson = JsonSerializer.Serialize(new
            {
                dbRoot = _activeDbRoot,
                hadLegacyProjectVariants = false,
                legacyProjectVariantCount = 0,
                remainingLegacyProjectVariantCount = 0,
                cleanedProjectJson = true,
                saved = false,
            });
            return true;
        }

        if (!TryLoadActiveProject(out DocProject project, out resultJson))
        {
            isError = true;
            return true;
        }

        SaveActiveProjectAndNotify(project);

        if (!TryReadLegacyProjectVariantInfo(projectJsonPath, out bool hasLegacyAfterSave, out int legacyProjectVariantCountAfterSave, out string postSaveReadError))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = postSaveReadError });
            return true;
        }

        bool cleanedProjectJson = !hasLegacyAfterSave && legacyProjectVariantCountAfterSave == 0;
        resultJson = JsonSerializer.Serialize(new
        {
            dbRoot = _activeDbRoot,
            hadLegacyProjectVariants = true,
            legacyProjectVariantCount,
            remainingLegacyProjectVariantCount = legacyProjectVariantCountAfterSave,
            cleanedProjectJson,
            saved = true,
        });
        return true;
    }

	    private bool ToolVariantList(JsonElement args, out string resultJson, out bool isError)
	    {
	        resultJson = "";
	        isError = false;

	        if (!TryLoadActiveProject(out DocProject project, out resultJson))
	        {
	            isError = true;
	            return true;
	        }

	        string tableId = GetArgString(args, "tableId");
	        if (string.IsNullOrWhiteSpace(tableId))
	        {
	            isError = true;
	            resultJson = JsonSerializer.Serialize(new { error = "Missing 'tableId'." });
	            return true;
	        }

	        DocTable? table = FindTable(project, tableId);
	        if (table == null)
	        {
	            isError = true;
	            resultJson = JsonSerializer.Serialize(new { error = $"Table '{tableId}' not found." });
	            return true;
	        }

        if (!DocSystemTableRules.AllowsVariants(table))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"System table '{table.Name}' does not support variants." });
            return true;
        }

	        var ordered = new List<DocTableVariant>(table.Variants.Count);
	        for (int variantIndex = 0; variantIndex < table.Variants.Count; variantIndex++)
	        {
	            ordered.Add(table.Variants[variantIndex].Clone());
	        }
	        ordered.Sort(static (leftVariant, rightVariant) => leftVariant.Id.CompareTo(rightVariant.Id));

	        var variants = new List<object>(ordered.Count + 1)
	        {
	            new { id = DocTableVariant.BaseVariantId, name = DocTableVariant.BaseVariantName },
	        };
	        for (int variantIndex = 0; variantIndex < ordered.Count; variantIndex++)
	        {
	            variants.Add(new { id = ordered[variantIndex].Id, name = ordered[variantIndex].Name });
	        }

	        resultJson = JsonSerializer.Serialize(new { tableId, variants });
	        return true;
	    }

	    private bool ToolVariantSet(JsonElement args, out string resultJson, out bool isError)
	    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out DocProject project, out resultJson))
        {
            isError = true;
            return true;
        }

	        if (!TryReadRequiredVariantIdArg(args, "variantId", out int variantId, out string variantIdError))
	        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = variantIdError });
            return true;
	        }

	        string tableId = GetArgString(args, "tableId");
	        if (string.IsNullOrWhiteSpace(tableId))
	        {
	            isError = true;
	            resultJson = JsonSerializer.Serialize(new { error = "Missing 'tableId'." });
	            return true;
	        }

	        DocTable? table = FindTable(project, tableId);
	        if (table == null)
	        {
	            isError = true;
	            resultJson = JsonSerializer.Serialize(new { error = $"Table '{tableId}' not found." });
	            return true;
	        }

        if (!DocSystemTableRules.AllowsVariants(table))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"System table '{table.Name}' does not support variants." });
            return true;
        }

	        if (table.IsDerived)
	        {
	            isError = true;
	            resultJson = JsonSerializer.Serialize(new { error = "Variants are not supported for derived tables." });
	            return true;
	        }

	        if (variantId <= DocTableVariant.BaseVariantId)
	        {
	            isError = true;
	            resultJson = JsonSerializer.Serialize(new { error = "variantId must be > 0. Base (0) is reserved." });
	            return true;
	        }

        string name = GetArgString(args, "name");
	        if (string.IsNullOrWhiteSpace(name))
	        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'name'." });
            return true;
	        }

	        int existingVariantIndex = FindTableVariantIndex(table, variantId);
	        for (int variantIndex = 0; variantIndex < table.Variants.Count; variantIndex++)
	        {
	            DocTableVariant variant = table.Variants[variantIndex];
	            if (variant.Id == variantId)
	            {
	                continue;
	            }

            if (string.Equals(variant.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new
                {
                    error = $"Variant name '{name}' already exists.",
                });
                return true;
            }
        }

	        bool created;
	        bool updated;
	        if (existingVariantIndex >= 0)
	        {
	            DocTableVariant existingVariant = table.Variants[existingVariantIndex];
	            updated = !string.Equals(existingVariant.Name, name, StringComparison.Ordinal);
	            existingVariant.Name = name;
	            created = false;
	        }
	        else
	        {
	            table.Variants.Add(new DocTableVariant
	            {
	                Id = variantId,
	                Name = name,
	            });
	            created = true;
	            updated = true;
	        }

	        table.Variants.Sort(static (leftVariant, rightVariant) => leftVariant.Id.CompareTo(rightVariant.Id));
	        SaveActiveProjectAndNotify(project);
	        resultJson = JsonSerializer.Serialize(new
	        {
	            tableId,
	            variantId,
	            name,
	            created,
	            updated,
	        });
	        return true;
	    }

	    private bool ToolVariantDelete(JsonElement args, out string resultJson, out bool isError)
	    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out DocProject project, out resultJson))
        {
            isError = true;
            return true;
        }

	        if (!TryReadRequiredVariantIdArg(args, "variantId", out int variantId, out string variantIdError))
	        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = variantIdError });
            return true;
	        }

	        string tableId = GetArgString(args, "tableId");
	        if (string.IsNullOrWhiteSpace(tableId))
	        {
	            isError = true;
	            resultJson = JsonSerializer.Serialize(new { error = "Missing 'tableId'." });
	            return true;
	        }

	        DocTable? table = FindTable(project, tableId);
	        if (table == null)
	        {
	            isError = true;
	            resultJson = JsonSerializer.Serialize(new { error = $"Table '{tableId}' not found." });
	            return true;
	        }

	        if (variantId == DocTableVariant.BaseVariantId)
	        {
	            isError = true;
	            resultJson = JsonSerializer.Serialize(new { error = "Base variant (id=0) cannot be deleted." });
	            return true;
	        }

	        int variantIndexToDelete = FindTableVariantIndex(table, variantId);
	        if (variantIndexToDelete < 0)
	        {
	            isError = true;
	            resultJson = JsonSerializer.Serialize(new { error = $"Variant '{variantId}' not found." });
	            return true;
	        }

	        table.Variants.RemoveAt(variantIndexToDelete);

	        int tablesUpdated = 0;
	        int deltaCountBefore = table.VariantDeltas.Count;
	        for (int deltaIndex = table.VariantDeltas.Count - 1; deltaIndex >= 0; deltaIndex--)
	        {
	            if (table.VariantDeltas[deltaIndex].VariantId == variantId)
	            {
	                table.VariantDeltas.RemoveAt(deltaIndex);
	            }
	        }
	        if (table.VariantDeltas.Count != deltaCountBefore)
	        {
	            tablesUpdated = 1;
	        }

	        int blocksResetToBase = 0;
	        for (int documentIndex = 0; documentIndex < project.Documents.Count; documentIndex++)
	        {
	            DocDocument document = project.Documents[documentIndex];
	            for (int blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
	            {
	                DocBlock block = document.Blocks[blockIndex];
	                if (block.Type == DocBlockType.Table &&
	                    string.Equals(block.TableId, tableId, StringComparison.Ordinal) &&
	                    block.TableVariantId == variantId)
	                {
	                    block.TableVariantId = DocTableVariant.BaseVariantId;
	                    blocksResetToBase++;
	                }
	            }
	        }

	        SaveActiveProjectAndNotify(project);
	        resultJson = JsonSerializer.Serialize(new
	        {
	            tableId,
	            variantId,
	            deleted = true,
	            tablesUpdated,
	            blocksResetToBase,
	        });
	        return true;
	    }

    private bool ToolFolderList(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        bool filterByScope = false;
        DocFolderScope scopeFilter = DocFolderScope.Tables;
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("scope", out var scopeElement))
        {
            if (scopeElement.ValueKind != JsonValueKind.String || !TryParseFolderScope(scopeElement.GetString(), out scopeFilter))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = "Invalid 'scope'. Expected Tables or Documents." });
                return true;
            }
            filterByScope = true;
        }

        var folders = new List<object>(project.Folders.Count);
        for (int folderIndex = 0; folderIndex < project.Folders.Count; folderIndex++)
        {
            var folder = project.Folders[folderIndex];
            if (filterByScope && folder.Scope != scopeFilter)
            {
                continue;
            }

            folders.Add(new
            {
                id = folder.Id,
                name = folder.Name,
                scope = folder.Scope.ToString(),
                parentFolderId = folder.ParentFolderId,
            });
        }

        resultJson = JsonSerializer.Serialize(new { folders });
        return true;
    }

    private bool ToolFolderCreate(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string name = GetArgString(args, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'name'." });
            return true;
        }

        string scopeString = GetArgString(args, "scope");
        if (!TryParseFolderScope(scopeString, out var scope))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing or invalid 'scope'. Expected Tables or Documents." });
            return true;
        }

        string? parentFolderId = GetArgStringOrNull(args, "parentFolderId");
        if (!string.IsNullOrWhiteSpace(parentFolderId))
        {
            DocFolder? parentFolder = FindFolder(project, parentFolderId);
            if (parentFolder == null)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"Parent folder '{parentFolderId}' not found." });
                return true;
            }

            if (parentFolder.Scope != scope)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"Parent folder scope mismatch. Expected '{scope}'." });
                return true;
            }
        }

        var folder = new DocFolder
        {
            Name = name,
            Scope = scope,
            ParentFolderId = string.IsNullOrWhiteSpace(parentFolderId) ? null : parentFolderId,
        };

        project.Folders.Add(folder);
        SaveActiveProjectAndNotify(project);
        resultJson = JsonSerializer.Serialize(new { folderId = folder.Id });
        return true;
    }

    private bool ToolTableList(out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        var tables = new List<object>(project.Tables.Count);
        for (int i = 0; i < project.Tables.Count; i++)
        {
            var t = project.Tables[i];
            tables.Add(new
            {
                id = t.Id,
                name = t.Name,
                fileName = t.FileName,
                folderId = t.FolderId,
                isDerived = t.IsDerived,
                isSchemaLinked = t.IsSchemaLinked,
                schemaSourceTableId = t.SchemaSourceTableId,
                isInherited = t.IsInherited,
                inheritanceSourceTableId = t.InheritanceSourceTableId,
                isSystemTable = t.IsSystemTable,
                systemKey = t.SystemKey,
                systemSchemaLocked = t.IsSystemSchemaLocked,
                systemDataLocked = t.IsSystemDataLocked,
                isSubtable = t.IsSubtable,
                parentTableId = t.ParentTableId
            });
        }

        resultJson = JsonSerializer.Serialize(new { tables });
        return true;
    }

    private bool ToolTableCreate(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string name = GetArgString(args, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'name'." });
            return true;
        }

        string fileName = GetArgString(args, "fileName");
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = SanitizeFileStem(name);
        }
        else
        {
            fileName = SanitizeFileStem(fileName);
        }

        string folderId = GetArgString(args, "folderId");
        string schemaSourceTableId = GetArgString(args, "schemaSourceTableId");
        string inheritanceSourceTableId = GetArgString(args, "inheritanceSourceTableId");

        if (!string.IsNullOrWhiteSpace(schemaSourceTableId) &&
            !string.IsNullOrWhiteSpace(inheritanceSourceTableId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Only one of 'schemaSourceTableId' or 'inheritanceSourceTableId' can be set." });
            return true;
        }

        var table = new DocTable
        {
            Name = name,
            FileName = fileName,
        };

        if (!string.IsNullOrWhiteSpace(folderId))
        {
            DocFolder? folder = null;
            for (int folderIndex = 0; folderIndex < project.Folders.Count; folderIndex++)
            {
                var candidateFolder = project.Folders[folderIndex];
                if (string.Equals(candidateFolder.Id, folderId, StringComparison.Ordinal))
                {
                    folder = candidateFolder;
                    break;
                }
            }

            if (folder == null || folder.Scope != DocFolderScope.Tables)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"Folder not found or invalid scope: {folderId}." });
                return true;
            }

            table.FolderId = folderId;
        }

        if (!string.IsNullOrWhiteSpace(schemaSourceTableId))
        {
            DocTable? sourceTable = FindTable(project, schemaSourceTableId);
            if (sourceTable == null)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"Schema source table '{schemaSourceTableId}' not found." });
                return true;
            }

            table.SchemaSourceTableId = schemaSourceTableId;
            project.Tables.Add(table);
            try
            {
                SchemaLinkedTableSynchronizer.Synchronize(project);
            }
            catch (Exception ex)
            {
                project.Tables.RemoveAt(project.Tables.Count - 1);
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = ex.Message });
                return true;
            }
        }
        else if (!string.IsNullOrWhiteSpace(inheritanceSourceTableId))
        {
            DocTable? sourceTable = FindTable(project, inheritanceSourceTableId);
            if (sourceTable == null)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"Inheritance source table '{inheritanceSourceTableId}' not found." });
                return true;
            }

            table.InheritanceSourceTableId = inheritanceSourceTableId;
            project.Tables.Add(table);
            try
            {
                SchemaLinkedTableSynchronizer.Synchronize(project);
            }
            catch (Exception ex)
            {
                project.Tables.RemoveAt(project.Tables.Count - 1);
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = ex.Message });
                return true;
            }
        }
        else
        {
            project.Tables.Add(table);
        }
        SaveActiveProjectAndNotify(project);

        resultJson = JsonSerializer.Serialize(new { tableId = table.Id });
        return true;
    }

    private bool ToolTableDelete(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "{}";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string tableId = GetArgString(args, "tableId");
        if (string.IsNullOrWhiteSpace(tableId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'tableId'." });
            return true;
        }

        int index = FindTableIndex(project, tableId);
        if (index < 0)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table not found: {tableId}." });
            return true;
        }

        DocTable targetTable = project.Tables[index];
        if (DocSystemTableRules.IsSystemTable(targetTable))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"System table '{targetTable.Name}' cannot be deleted." });
            return true;
        }

        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable table = project.Tables[tableIndex];
            if (tableIndex == index)
            {
                continue;
            }

            if (string.Equals(table.SchemaSourceTableId, tableId, StringComparison.Ordinal))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new
                {
                    error = $"Cannot delete table '{tableId}' because schema-linked table '{table.Name}' depends on it.",
                });
                return true;
            }

            if (string.Equals(table.InheritanceSourceTableId, tableId, StringComparison.Ordinal))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new
                {
                    error = $"Cannot delete table '{tableId}' because inherited table '{table.Name}' depends on it.",
                });
                return true;
            }
        }

        project.Tables.RemoveAt(index);
        SaveActiveProjectAndNotify(project);
        resultJson = "{}";
        return true;
    }

    private bool ToolTableFolderSet(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string tableId = GetArgString(args, "tableId");
        if (string.IsNullOrWhiteSpace(tableId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'tableId'." });
            return true;
        }

        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("folderId", out var folderElement))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'folderId'. Pass empty string to clear folder." });
            return true;
        }

        string? folderId = folderElement.ValueKind switch
        {
            JsonValueKind.Null => "",
            JsonValueKind.String => folderElement.GetString(),
            _ => null,
        };

        if (folderId == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Invalid 'folderId'. Must be string or null." });
            return true;
        }

        var table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table '{tableId}' not found." });
            return true;
        }

        if (DocSystemTableRules.IsSystemTable(table))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"System table '{table.Name}' cannot be moved to a folder." });
            return true;
        }

        if (!string.IsNullOrWhiteSpace(folderId))
        {
            DocFolder? folder = FindFolder(project, folderId);
            if (folder == null || folder.Scope != DocFolderScope.Tables)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"Folder '{folderId}' not found or not Tables scope." });
                return true;
            }
        }

        table.FolderId = string.IsNullOrWhiteSpace(folderId) ? null : folderId;
        SaveActiveProjectAndNotify(project);
        resultJson = JsonSerializer.Serialize(new
        {
            tableId = table.Id,
            folderId = table.FolderId ?? "",
            updated = true,
        });
        return true;
    }

    private bool ToolTableSchemaGet(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string tableId = GetArgString(args, "tableId");
        if (string.IsNullOrWhiteSpace(tableId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'tableId'." });
            return true;
        }

        var table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table not found: {tableId}." });
            return true;
        }

        resultJson = JsonSerializer.Serialize(new
        {
            table = new
            {
                id = table.Id,
                name = table.Name,
                fileName = table.FileName,
                isDerived = table.IsDerived,
                isSchemaLinked = table.IsSchemaLinked,
                schemaSourceTableId = table.SchemaSourceTableId,
                isInherited = table.IsInherited,
                inheritanceSourceTableId = table.InheritanceSourceTableId,
                isSystemTable = table.IsSystemTable,
                systemKey = table.SystemKey,
                systemSchemaLocked = table.IsSystemSchemaLocked,
                systemDataLocked = table.IsSystemDataLocked,
                isSubtable = table.IsSubtable,
                parentTableId = table.ParentTableId,
                exportConfig = table.ExportConfig,
                keys = table.Keys,
                variantDeltaCount = table.VariantDeltas.Count,
                columns = table.Columns,
            }
        });
        return true;
    }

    private bool ToolTableSchemaLinkSet(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out DocProject project, out resultJson))
        {
            isError = true;
            return true;
        }

        string tableId = GetArgString(args, "tableId");
        if (string.IsNullOrWhiteSpace(tableId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'tableId'." });
            return true;
        }

        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("schemaSourceTableId", out JsonElement sourceElement))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'schemaSourceTableId'. Pass null or empty string to clear." });
            return true;
        }

        string? sourceTableId = sourceElement.ValueKind switch
        {
            JsonValueKind.Null => "",
            JsonValueKind.String => sourceElement.GetString(),
            _ => null,
        };

        if (sourceTableId == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Invalid 'schemaSourceTableId'. Must be string or null." });
            return true;
        }

        DocTable? table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table '{tableId}' not found." });
            return true;
        }

        if (DocSystemTableRules.IsSystemTable(table))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"System table '{table.Name}' does not allow schema-link changes." });
            return true;
        }

        if (table.IsDerived)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Derived tables cannot be schema-linked." });
            return true;
        }

        if (table.IsInherited)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Inherited tables cannot be schema-linked." });
            return true;
        }

        string previousSourceTableId = table.SchemaSourceTableId ?? "";
        string normalizedSourceTableId = sourceTableId ?? "";
        if (!string.IsNullOrWhiteSpace(normalizedSourceTableId))
        {
            if (string.Equals(normalizedSourceTableId, table.Id, StringComparison.Ordinal))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = "A table cannot schema-link to itself." });
                return true;
            }

            if (FindTable(project, normalizedSourceTableId) == null)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"Schema source table '{normalizedSourceTableId}' not found." });
                return true;
            }
        }

        table.SchemaSourceTableId = string.IsNullOrWhiteSpace(normalizedSourceTableId) ? null : normalizedSourceTableId;
        try
        {
            SchemaLinkedTableSynchronizer.Synchronize(project);
        }
        catch (Exception ex)
        {
            table.SchemaSourceTableId = string.IsNullOrWhiteSpace(previousSourceTableId) ? null : previousSourceTableId;
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = ex.Message });
            return true;
        }

        SaveActiveProjectAndNotify(project);
        resultJson = JsonSerializer.Serialize(new
        {
            tableId = table.Id,
            schemaSourceTableId = table.SchemaSourceTableId ?? "",
            isSchemaLinked = table.IsSchemaLinked,
            updated = true,
        });
        return true;
    }

    private bool ToolTableInheritanceSet(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out DocProject project, out resultJson))
        {
            isError = true;
            return true;
        }

        string tableId = GetArgString(args, "tableId");
        if (string.IsNullOrWhiteSpace(tableId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'tableId'." });
            return true;
        }

        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("inheritanceSourceTableId", out JsonElement sourceElement))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'inheritanceSourceTableId'. Pass null or empty string to clear." });
            return true;
        }

        string? sourceTableId = sourceElement.ValueKind switch
        {
            JsonValueKind.Null => "",
            JsonValueKind.String => sourceElement.GetString(),
            _ => null,
        };

        if (sourceTableId == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Invalid 'inheritanceSourceTableId'. Must be string or null." });
            return true;
        }

        DocTable? table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table '{tableId}' not found." });
            return true;
        }

        if (DocSystemTableRules.IsSystemTable(table))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"System table '{table.Name}' does not allow inheritance changes." });
            return true;
        }

        if (table.IsDerived)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Derived tables cannot use inheritance." });
            return true;
        }

        if (table.IsSchemaLinked)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Schema-linked tables cannot use inheritance." });
            return true;
        }

        string previousSourceTableId = table.InheritanceSourceTableId ?? "";
        string normalizedSourceTableId = sourceTableId ?? "";
        if (!string.IsNullOrWhiteSpace(normalizedSourceTableId))
        {
            if (string.Equals(normalizedSourceTableId, table.Id, StringComparison.Ordinal))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = "A table cannot inherit from itself." });
                return true;
            }

            if (FindTable(project, normalizedSourceTableId) == null)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"Inheritance source table '{normalizedSourceTableId}' not found." });
                return true;
            }
        }

        table.InheritanceSourceTableId = string.IsNullOrWhiteSpace(normalizedSourceTableId) ? null : normalizedSourceTableId;
        try
        {
            SchemaLinkedTableSynchronizer.Synchronize(project);
        }
        catch (Exception ex)
        {
            table.InheritanceSourceTableId = string.IsNullOrWhiteSpace(previousSourceTableId) ? null : previousSourceTableId;
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = ex.Message });
            return true;
        }

        SaveActiveProjectAndNotify(project);
        resultJson = JsonSerializer.Serialize(new
        {
            tableId = table.Id,
            inheritanceSourceTableId = table.InheritanceSourceTableId ?? "",
            isInherited = table.IsInherited,
            updated = true,
        });
        return true;
    }

    private bool ToolColumnAdd(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string tableId = GetArgString(args, "tableId");
        string name = GetArgString(args, "name");
        string kindRaw = GetArgString(args, "kind");
        string typeIdRaw = GetArgString(args, "typeId");

        if (string.IsNullOrWhiteSpace(tableId) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(kindRaw))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing required fields: tableId, name, kind." });
            return true;
        }

        var table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table not found: {tableId}." });
            return true;
        }

        if (DocSystemTableRules.IsSchemaLocked(table))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new
            {
                error = $"System table '{table.Name}' has a locked schema.",
            });
            return true;
        }

        if (table.IsSchemaLinked)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new
            {
                error = $"Table '{table.Name}' is schema-linked and cannot modify columns locally.",
            });
            return true;
        }

        if (!TryParseColumnKind(kindRaw, out var kind))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Invalid column kind: {kindRaw}." });
            return true;
        }

        var col = new DocColumn
        {
            Name = name,
            Kind = kind,
            ColumnTypeId = string.IsNullOrWhiteSpace(typeIdRaw)
                ? DocColumnTypeIdMapper.FromKind(kind)
                : typeIdRaw,
        };

        if (args.ValueKind == JsonValueKind.Object)
        {
            if (args.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number)
            {
                col.Width = (float)w.GetDouble();
            }
            if (args.TryGetProperty("typeId", out var tid) && (tid.ValueKind == JsonValueKind.String || tid.ValueKind == JsonValueKind.Null))
            {
                col.ColumnTypeId = tid.ValueKind == JsonValueKind.Null
                    ? DocColumnTypeIdMapper.FromKind(col.Kind)
                    : tid.GetString() ?? "";
            }
            if (args.TryGetProperty("formulaExpression", out var fe) && fe.ValueKind == JsonValueKind.String)
            {
                col.FormulaExpression = fe.GetString() ?? "";
            }
            if (args.TryGetProperty("relationTableId", out var rt) && rt.ValueKind == JsonValueKind.String)
            {
                col.RelationTableId = rt.GetString();
            }
            if (args.TryGetProperty("relationTargetMode", out var rtm) && rtm.ValueKind == JsonValueKind.String)
            {
                string rawRelationTargetMode = rtm.GetString() ?? "";
                if (!Enum.TryParse<DocRelationTargetMode>(rawRelationTargetMode, ignoreCase: true, out var parsedRelationTargetMode))
                {
                    isError = true;
                    resultJson = JsonSerializer.Serialize(new { error = $"Invalid relationTargetMode: {rawRelationTargetMode}." });
                    return true;
                }

                col.RelationTargetMode = parsedRelationTargetMode;
            }
            if (args.TryGetProperty("relationTableVariantId", out var rtv) && rtv.ValueKind == JsonValueKind.Number)
            {
                col.RelationTableVariantId = rtv.GetInt32();
            }
            if (args.TryGetProperty("relationDisplayColumnId", out var rdc) && (rdc.ValueKind == JsonValueKind.String || rdc.ValueKind == JsonValueKind.Null))
            {
                col.RelationDisplayColumnId = rdc.ValueKind == JsonValueKind.Null ? null : rdc.GetString();
            }
            if (args.TryGetProperty("options", out var opt) && opt.ValueKind == JsonValueKind.Array)
            {
                var options = new List<string>();
                foreach (var o in opt.EnumerateArray())
                {
                    if (o.ValueKind == JsonValueKind.String)
                    {
                        options.Add(o.GetString() ?? "");
                    }
                }
                col.Options = options;
            }
            if (TryReadFormulaEvalScopes(args, out var formulaEvalScopes))
            {
                col.FormulaEvalScopes = formulaEvalScopes;
            }
        }

        if (col.Kind == DocColumnKind.Relation)
        {
            col.RelationTableId = DocRelationTargetResolver.ResolveTargetTableId(table, col);
            if (string.IsNullOrWhiteSpace(col.RelationTableId))
            {
                col.RelationTableVariantId = 0;
            }
        }

        string? childTableId = null;
        if (kind == DocColumnKind.Subtable)
        {
            // Auto-create child table
            int count = project.Tables.Count;
            var parentRowCol = new DocColumn { Name = "_parentRowId", Kind = DocColumnKind.Text, IsHidden = true, Width = 100f };
            var itemCol = new DocColumn { Name = "Item", Kind = DocColumnKind.Text, Width = 150f };
            var childTable = new DocTable
            {
                Name = $"{table.Name}_{name}",
                FileName = $"subtable{count + 1}",
                ParentTableId = table.Id,
                ParentRowColumnId = parentRowCol.Id,
            };
            childTable.Columns.Add(parentRowCol);
            childTable.Columns.Add(itemCol);
            col.SubtableId = childTable.Id;
            childTableId = childTable.Id;
            project.Tables.Add(childTable);
        }

        table.Columns.Add(col);
        for (int i = 0; i < table.Rows.Count; i++)
        {
            table.Rows[i].SetCell(col.Id, DocCellValue.Default(col));
        }

        SaveActiveProjectAndNotify(project);
        if (childTableId != null)
            resultJson = JsonSerializer.Serialize(new { columnId = col.Id, childTableId });
        else
            resultJson = JsonSerializer.Serialize(new { columnId = col.Id });
        return true;
    }

    private bool ToolColumnUpdate(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "{}";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string tableId = GetArgString(args, "tableId");
        string columnId = GetArgString(args, "columnId");
        if (string.IsNullOrWhiteSpace(tableId) || string.IsNullOrWhiteSpace(columnId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing required fields: tableId, columnId." });
            return true;
        }

        var table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table not found: {tableId}." });
            return true;
        }

        if (DocSystemTableRules.IsSchemaLocked(table))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new
            {
                error = $"System table '{table.Name}' has a locked schema.",
            });
            return true;
        }

        if (table.IsSchemaLinked)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new
            {
                error = $"Table '{table.Name}' is schema-linked and cannot modify columns locally.",
            });
            return true;
        }

        var col = FindColumn(table, columnId);
        if (col == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Column not found: {columnId}." });
            return true;
        }

        if (table.IsInherited && col.IsInherited)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new
            {
                error = $"Column '{col.Name}' is inherited and cannot be modified locally.",
            });
            return true;
        }

        if (args.ValueKind == JsonValueKind.Object)
        {
            if (args.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
            {
                col.Name = n.GetString() ?? col.Name;
            }
            if (args.TryGetProperty("typeId", out var tid) && (tid.ValueKind == JsonValueKind.String || tid.ValueKind == JsonValueKind.Null))
            {
                col.ColumnTypeId = tid.ValueKind == JsonValueKind.Null
                    ? DocColumnTypeIdMapper.FromKind(col.Kind)
                    : tid.GetString() ?? "";
            }
            if (args.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number)
            {
                col.Width = (float)w.GetDouble();
            }
            if (args.TryGetProperty("options", out var opt) && opt.ValueKind == JsonValueKind.Array)
            {
                var options = new List<string>();
                foreach (var o in opt.EnumerateArray())
                {
                    if (o.ValueKind == JsonValueKind.String)
                    {
                        options.Add(o.GetString() ?? "");
                    }
                }
                col.Options = options;
            }
            if (args.TryGetProperty("formulaExpression", out var fe) && fe.ValueKind == JsonValueKind.String)
            {
                col.FormulaExpression = fe.GetString() ?? "";
            }
            if (args.TryGetProperty("relationTableId", out var rt) && (rt.ValueKind == JsonValueKind.String || rt.ValueKind == JsonValueKind.Null))
            {
                col.RelationTableId = rt.ValueKind == JsonValueKind.Null ? null : rt.GetString();
                if (col.RelationTableId == null)
                {
                    col.RelationTableVariantId = 0;
                }
            }
            if (args.TryGetProperty("relationTargetMode", out var rtm) && (rtm.ValueKind == JsonValueKind.String || rtm.ValueKind == JsonValueKind.Null))
            {
                if (rtm.ValueKind == JsonValueKind.Null)
                {
                    col.RelationTargetMode = DocRelationTargetMode.ExternalTable;
                }
                else
                {
                    string rawRelationTargetMode = rtm.GetString() ?? "";
                    if (!Enum.TryParse<DocRelationTargetMode>(rawRelationTargetMode, ignoreCase: true, out var parsedRelationTargetMode))
                    {
                        isError = true;
                        resultJson = JsonSerializer.Serialize(new { error = $"Invalid relationTargetMode: {rawRelationTargetMode}." });
                        return true;
                    }

                    col.RelationTargetMode = parsedRelationTargetMode;
                }
            }
            if (args.TryGetProperty("relationTableVariantId", out var rtv) && rtv.ValueKind == JsonValueKind.Number)
            {
                col.RelationTableVariantId = rtv.GetInt32();
            }
            if (args.TryGetProperty("relationDisplayColumnId", out var rdc) && (rdc.ValueKind == JsonValueKind.String || rdc.ValueKind == JsonValueKind.Null))
            {
                col.RelationDisplayColumnId = rdc.ValueKind == JsonValueKind.Null ? null : rdc.GetString();
            }
            if (args.TryGetProperty("exportIgnore", out var ei) && (ei.ValueKind == JsonValueKind.True || ei.ValueKind == JsonValueKind.False))
            {
                col.ExportIgnore = ei.GetBoolean();
            }
            if (args.TryGetProperty("exportType", out var et) && (et.ValueKind == JsonValueKind.String || et.ValueKind == JsonValueKind.Null))
            {
                col.ExportType = et.ValueKind == JsonValueKind.Null ? null : et.GetString();
            }
            if (args.TryGetProperty("exportEnumName", out var en) && (en.ValueKind == JsonValueKind.String || en.ValueKind == JsonValueKind.Null))
            {
                col.ExportEnumName = en.ValueKind == JsonValueKind.Null ? null : en.GetString();
            }
            if (TryReadFormulaEvalScopes(args, out var formulaEvalScopes))
            {
                col.FormulaEvalScopes = formulaEvalScopes;
            }
        }

        if (col.Kind == DocColumnKind.Relation)
        {
            col.RelationTableId = DocRelationTargetResolver.ResolveTargetTableId(table, col);
            if (string.IsNullOrWhiteSpace(col.RelationTableId))
            {
                col.RelationTableVariantId = 0;
            }
        }

        SaveActiveProjectAndNotify(project);
        resultJson = "{}";
        return true;
    }

    private bool ToolColumnDelete(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "{}";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string tableId = GetArgString(args, "tableId");
        string columnId = GetArgString(args, "columnId");
        if (string.IsNullOrWhiteSpace(tableId) || string.IsNullOrWhiteSpace(columnId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing required fields: tableId, columnId." });
            return true;
        }

        var table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table not found: {tableId}." });
            return true;
        }

        if (DocSystemTableRules.IsSchemaLocked(table))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new
            {
                error = $"System table '{table.Name}' has a locked schema.",
            });
            return true;
        }

        if (table.IsSchemaLinked)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new
            {
                error = $"Table '{table.Name}' is schema-linked and cannot modify columns locally.",
            });
            return true;
        }

        int colIndex = FindColumnIndex(table, columnId);
        if (colIndex < 0)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Column not found: {columnId}." });
            return true;
        }

        DocColumn column = table.Columns[colIndex];
        if (table.IsInherited && column.IsInherited)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new
            {
                error = $"Column '{column.Name}' is inherited and cannot be deleted locally.",
            });
            return true;
        }

        table.Columns.RemoveAt(colIndex);
        for (int i = 0; i < table.Rows.Count; i++)
        {
            table.Rows[i].Cells.Remove(columnId);
        }

        if (string.Equals(table.Keys.PrimaryKeyColumnId, columnId, StringComparison.Ordinal))
        {
            table.Keys.PrimaryKeyColumnId = "";
        }
        for (int i = table.Keys.SecondaryKeys.Count - 1; i >= 0; i--)
        {
            if (string.Equals(table.Keys.SecondaryKeys[i].ColumnId, columnId, StringComparison.Ordinal))
            {
                table.Keys.SecondaryKeys.RemoveAt(i);
            }
        }

        SaveActiveProjectAndNotify(project);
        resultJson = "{}";
        return true;
    }

    private bool ToolTableExportSet(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "{}";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string tableId = GetArgString(args, "tableId");
        if (string.IsNullOrWhiteSpace(tableId) || args.ValueKind != JsonValueKind.Object)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing tableId." });
            return true;
        }

        var table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table not found: {tableId}." });
            return true;
        }

        if (DocSystemTableRules.IsSchemaLocked(table))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"System table '{table.Name}' does not allow export config edits." });
            return true;
        }

        if (!args.TryGetProperty("enabled", out var enabledProp) || (enabledProp.ValueKind != JsonValueKind.True && enabledProp.ValueKind != JsonValueKind.False))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing enabled." });
            return true;
        }

        bool enabled = enabledProp.GetBoolean();
        table.ExportConfig ??= new DocTableExportConfig();
        table.ExportConfig.Enabled = enabled;

        SaveActiveProjectAndNotify(project);
        resultJson = "{}";
        return true;
    }

    private bool ToolTableKeysSet(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "{}";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string tableId = GetArgString(args, "tableId");
        if (string.IsNullOrWhiteSpace(tableId) || args.ValueKind != JsonValueKind.Object)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing tableId." });
            return true;
        }

        var table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table not found: {tableId}." });
            return true;
        }

        if (DocSystemTableRules.IsSchemaLocked(table))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"System table '{table.Name}' does not allow key edits." });
            return true;
        }

        if (args.TryGetProperty("primaryKeyColumnId", out var pk) && pk.ValueKind == JsonValueKind.String)
        {
            table.Keys.PrimaryKeyColumnId = pk.GetString() ?? "";
        }

        if (args.TryGetProperty("secondaryKeys", out var sk) && sk.ValueKind == JsonValueKind.Array)
        {
            table.Keys.SecondaryKeys.Clear();
            foreach (var item in sk.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                string colId = GetStringOrEmpty(item, "columnId");
                bool unique = item.TryGetProperty("unique", out var u) && (u.ValueKind == JsonValueKind.True || u.ValueKind == JsonValueKind.False) && u.GetBoolean();
                if (!string.IsNullOrWhiteSpace(colId))
                {
                    table.Keys.SecondaryKeys.Add(new DocSecondaryKey { ColumnId = colId, Unique = unique });
                }
            }
        }

        SaveActiveProjectAndNotify(project);
        resultJson = "{}";
        return true;
    }

    private bool ToolRowAdd(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string tableId = GetArgString(args, "tableId");
        if (string.IsNullOrWhiteSpace(tableId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing tableId." });
            return true;
        }

        var table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table not found: {tableId}." });
            return true;
        }

        if (DocSystemTableRules.IsDataLocked(table))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"System table '{table.Name}' has locked row data." });
            return true;
        }

        if (!TryReadOptionalVariantIdArg(args, "variantId", DocTableVariant.BaseVariantId, out int variantId, out string variantError))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = variantError });
            return true;
        }

        if (!HasTableVariant(table, variantId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Variant '{variantId}' not found." });
            return true;
        }

        var row = new DocRow();
        string rowId = GetArgString(args, "rowId");
        if (!string.IsNullOrWhiteSpace(rowId))
        {
            row.Id = rowId;
        }

        for (int i = 0; i < table.Columns.Count; i++)
        {
            var col = table.Columns[i];
            row.SetCell(col.Id, DocCellValue.Default(col));
        }

        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("cells", out var cells) && cells.ValueKind == JsonValueKind.Object)
        {
            ApplyCells(table, row, cells);
        }

        if (variantId == DocTableVariant.BaseVariantId)
        {
            table.Rows.Add(row);
        }
        else
        {
            if (table.IsDerived)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new
                {
                    error = "Variant row add is not supported for derived tables.",
                });
                return true;
            }

            DocTableVariantDelta variantDelta = GetOrCreateVariantDelta(table, variantId);
            if (FindVariantRow(table, variantDelta, row.Id, out _, out _) != null)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"Row '{row.Id}' already exists in variant '{variantId}'." });
                return true;
            }

            variantDelta.AddedRows.Add(row);
        }

        NormalizeEditableSystemTablesIfNeeded(project, table, variantId);
        SaveActiveProjectAndNotify(project);
        resultJson = JsonSerializer.Serialize(new
        {
            variantId,
            rowId = row.Id,
        });
        return true;
    }

    private bool ToolRowAddBatch(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string tableId = GetArgString(args, "tableId");
        if (string.IsNullOrWhiteSpace(tableId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing tableId." });
            return true;
        }

        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("rows", out var rows) ||
            rows.ValueKind != JsonValueKind.Array)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing rows array." });
            return true;
        }

        var table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table not found: {tableId}." });
            return true;
        }

        if (DocSystemTableRules.IsDataLocked(table))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"System table '{table.Name}' has locked row data." });
            return true;
        }

        if (!TryReadOptionalVariantIdArg(args, "variantId", DocTableVariant.BaseVariantId, out int variantId, out string variantError))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = variantError });
            return true;
        }

        if (!HasTableVariant(table, variantId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Variant '{variantId}' not found." });
            return true;
        }

        if (variantId != DocTableVariant.BaseVariantId && table.IsDerived)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new
            {
                error = "Variant row add is not supported for derived tables.",
            });
            return true;
        }

        DocTableVariantDelta? variantDelta = null;
        if (variantId != DocTableVariant.BaseVariantId)
        {
            variantDelta = GetOrCreateVariantDelta(table, variantId);
        }

        var addedRowIds = new List<string>();
        var errors = new List<object>();

        foreach (var rowEntry in rows.EnumerateArray())
        {
            if (rowEntry.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new { rowId = "", error = "Invalid row entry: expected object." });
                continue;
            }

            var row = new DocRow();
            string rowId = GetStringOrEmpty(rowEntry, "rowId");
            if (!string.IsNullOrWhiteSpace(rowId))
            {
                row.Id = rowId;
            }

            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                var column = table.Columns[columnIndex];
                row.SetCell(column.Id, DocCellValue.Default(column));
            }

            if (rowEntry.TryGetProperty("cells", out var cells))
            {
                if (cells.ValueKind != JsonValueKind.Object)
                {
                    errors.Add(new { rowId = row.Id, error = "Invalid cells value: expected object." });
                    continue;
                }

                ApplyCells(table, row, cells);
            }

            if (variantId == DocTableVariant.BaseVariantId)
            {
                table.Rows.Add(row);
            }
            else
            {
                if (variantDelta != null &&
                    FindVariantRow(table, variantDelta, row.Id, out _, out _) != null)
                {
                    errors.Add(new { rowId = row.Id, error = $"Row '{row.Id}' already exists in variant '{variantId}'." });
                    continue;
                }

                variantDelta?.AddedRows.Add(row);
            }

            addedRowIds.Add(row.Id);
        }

        if (addedRowIds.Count > 0)
        {
            NormalizeEditableSystemTablesIfNeeded(project, table, variantId);
            SaveActiveProjectAndNotify(project);
        }

        resultJson = JsonSerializer.Serialize(new
        {
            variantId,
            addedCount = addedRowIds.Count,
            addedRowIds,
            errors,
        });
        return true;
    }

    private bool ToolRowUpdate(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "{}";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string tableId = GetArgString(args, "tableId");
        string rowId = GetArgString(args, "rowId");
        if (string.IsNullOrWhiteSpace(tableId) || string.IsNullOrWhiteSpace(rowId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing tableId or rowId." });
            return true;
        }

        var table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table not found: {tableId}." });
            return true;
        }

        if (DocSystemTableRules.IsDataLocked(table))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"System table '{table.Name}' has locked row data." });
            return true;
        }

        if (!TryReadOptionalVariantIdArg(args, "variantId", DocTableVariant.BaseVariantId, out int variantId, out string variantError))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = variantError });
            return true;
        }

        if (!HasTableVariant(table, variantId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Variant '{variantId}' not found." });
            return true;
        }

        if (variantId != DocTableVariant.BaseVariantId && table.IsDerived)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Variant row update is not supported for derived tables." });
            return true;
        }

        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("cells", out JsonElement cells) || cells.ValueKind != JsonValueKind.Object)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing cells object." });
            return true;
        }

        if (variantId == DocTableVariant.BaseVariantId)
        {
            DocRow? row = FindRow(table, rowId);
            if (row == null)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"Row not found: {rowId}." });
                return true;
            }

            ApplyCells(table, row, cells);
        }
        else
        {
            DocTableVariantDelta variantDelta = GetOrCreateVariantDelta(table, variantId);
            DocRow? row = FindVariantRow(table, variantDelta, rowId, out bool rowIsAdded, out bool rowIsDeletedBase);
            if (row == null || rowIsDeletedBase)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"Row not found in variant '{variantId}': {rowId}." });
                return true;
            }

            if (rowIsAdded)
            {
                ApplyCells(table, row, cells);
            }
            else
            {
                ApplyVariantCellOverrides(table, variantDelta, rowId, cells);
            }
        }

        NormalizeEditableSystemTablesIfNeeded(project, table, variantId);
        SaveActiveProjectAndNotify(project);
        resultJson = JsonSerializer.Serialize(new { variantId, updated = true });
        return true;
    }

    private bool ToolRowUpdateBatch(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string tableId = GetArgString(args, "tableId");
        if (string.IsNullOrWhiteSpace(tableId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing tableId." });
            return true;
        }

        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("updates", out var updates) ||
            updates.ValueKind != JsonValueKind.Array)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing updates array." });
            return true;
        }

        var table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table not found: {tableId}." });
            return true;
        }

        if (DocSystemTableRules.IsDataLocked(table))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"System table '{table.Name}' has locked row data." });
            return true;
        }

        if (!TryReadOptionalVariantIdArg(args, "variantId", DocTableVariant.BaseVariantId, out int variantId, out string variantError))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = variantError });
            return true;
        }

        if (!HasTableVariant(table, variantId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Variant '{variantId}' not found." });
            return true;
        }

        if (variantId != DocTableVariant.BaseVariantId && table.IsDerived)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Variant row update is not supported for derived tables." });
            return true;
        }

        DocTableVariantDelta? variantDelta = null;
        if (variantId != DocTableVariant.BaseVariantId)
        {
            variantDelta = GetOrCreateVariantDelta(table, variantId);
        }

        var updatedRowIds = new List<string>();
        var errors = new List<object>();

        foreach (var update in updates.EnumerateArray())
        {
            if (update.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new { rowId = "", error = "Invalid update entry: expected object." });
                continue;
            }

            string rowId = GetStringOrEmpty(update, "rowId");
            if (string.IsNullOrWhiteSpace(rowId))
            {
                errors.Add(new { rowId = "", error = "Missing rowId." });
                continue;
            }

            if (!update.TryGetProperty("cells", out JsonElement cells) || cells.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new { rowId, error = "Missing cells object." });
                continue;
            }

            if (variantId == DocTableVariant.BaseVariantId)
            {
                DocRow? baseRow = FindRow(table, rowId);
                if (baseRow == null)
                {
                    errors.Add(new { rowId, error = $"Row not found: {rowId}." });
                    continue;
                }

                ApplyCells(table, baseRow, cells);
            }
            else
            {
                DocRow? row = FindVariantRow(table, variantDelta!, rowId, out bool rowIsAdded, out bool rowIsDeletedBase);
                if (row == null || rowIsDeletedBase)
                {
                    errors.Add(new { rowId, error = $"Row not found in variant '{variantId}': {rowId}." });
                    continue;
                }

                if (rowIsAdded)
                {
                    ApplyCells(table, row, cells);
                }
                else
                {
                    ApplyVariantCellOverrides(table, variantDelta!, rowId, cells);
                }
            }

            updatedRowIds.Add(rowId);
        }

        if (updatedRowIds.Count > 0)
        {
            NormalizeEditableSystemTablesIfNeeded(project, table, variantId);
            SaveActiveProjectAndNotify(project);
        }

        resultJson = JsonSerializer.Serialize(new
        {
            variantId,
            updatedCount = updatedRowIds.Count,
            updatedRowIds,
            errors,
        });
        return true;
    }

    private bool ToolRowDelete(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "{}";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string tableId = GetArgString(args, "tableId");
        string rowId = GetArgString(args, "rowId");
        if (string.IsNullOrWhiteSpace(tableId) || string.IsNullOrWhiteSpace(rowId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing tableId or rowId." });
            return true;
        }

        var table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table not found: {tableId}." });
            return true;
        }

        if (DocSystemTableRules.IsDataLocked(table))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"System table '{table.Name}' has locked row data." });
            return true;
        }

        if (!TryReadOptionalVariantIdArg(args, "variantId", DocTableVariant.BaseVariantId, out int variantId, out string variantError))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = variantError });
            return true;
        }

        if (!HasTableVariant(table, variantId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Variant '{variantId}' not found." });
            return true;
        }

        if (variantId == DocTableVariant.BaseVariantId)
        {
            int rowIndex = FindRowIndex(table, rowId);
            if (rowIndex < 0)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"Row not found: {rowId}." });
                return true;
            }

            table.Rows.RemoveAt(rowIndex);
        }
        else
        {
            if (table.IsDerived)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new
                {
                    error = "Variant row delete is not supported for derived tables.",
                });
                return true;
            }

            DocTableVariantDelta variantDelta = GetOrCreateVariantDelta(table, variantId);
            int addedRowIndex = FindAddedRowIndex(variantDelta, rowId);
            if (addedRowIndex >= 0)
            {
                variantDelta.AddedRows.RemoveAt(addedRowIndex);
                RemoveVariantCellOverridesForRow(variantDelta, rowId);
            }
            else if (FindRowIndex(table, rowId) >= 0)
            {
                if (!variantDelta.DeletedBaseRowIds.Contains(rowId))
                {
                    variantDelta.DeletedBaseRowIds.Add(rowId);
                }

                RemoveVariantCellOverridesForRow(variantDelta, rowId);
            }
            else
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"Row not found in variant '{variantId}': {rowId}." });
                return true;
            }
        }

        NormalizeEditableSystemTablesIfNeeded(project, table, variantId);
        SaveActiveProjectAndNotify(project);
        resultJson = JsonSerializer.Serialize(new { variantId, deleted = true });
        return true;
    }

    private bool ToolRowDeleteBatch(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string tableId = GetArgString(args, "tableId");
        if (string.IsNullOrWhiteSpace(tableId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing tableId." });
            return true;
        }

        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("rowIds", out var rowIds) ||
            rowIds.ValueKind != JsonValueKind.Array)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing rowIds array." });
            return true;
        }

        var table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table not found: {tableId}." });
            return true;
        }

        if (DocSystemTableRules.IsDataLocked(table))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"System table '{table.Name}' has locked row data." });
            return true;
        }

        if (!TryReadOptionalVariantIdArg(args, "variantId", DocTableVariant.BaseVariantId, out int variantId, out string variantError))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = variantError });
            return true;
        }

        if (!HasTableVariant(table, variantId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Variant '{variantId}' not found." });
            return true;
        }

        if (variantId != DocTableVariant.BaseVariantId && table.IsDerived)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new
            {
                error = "Variant row delete is not supported for derived tables.",
            });
            return true;
        }

        DocTableVariantDelta? variantDelta = null;
        if (variantId != DocTableVariant.BaseVariantId)
        {
            variantDelta = GetOrCreateVariantDelta(table, variantId);
        }

        var deletedRowIds = new List<string>();
        var errors = new List<object>();

        foreach (var rowIdElement in rowIds.EnumerateArray())
        {
            if (rowIdElement.ValueKind != JsonValueKind.String)
            {
                errors.Add(new { rowId = "", error = "Invalid rowId entry: expected string." });
                continue;
            }

            string rowId = rowIdElement.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(rowId))
            {
                errors.Add(new { rowId = "", error = "Missing rowId." });
                continue;
            }

            if (variantId == DocTableVariant.BaseVariantId)
            {
                int rowIndex = FindRowIndex(table, rowId);
                if (rowIndex < 0)
                {
                    errors.Add(new { rowId, error = $"Row not found: {rowId}." });
                    continue;
                }

                table.Rows.RemoveAt(rowIndex);
                deletedRowIds.Add(rowId);
                continue;
            }

            int addedRowIndex = FindAddedRowIndex(variantDelta!, rowId);
            if (addedRowIndex >= 0)
            {
                variantDelta!.AddedRows.RemoveAt(addedRowIndex);
                RemoveVariantCellOverridesForRow(variantDelta, rowId);
                deletedRowIds.Add(rowId);
                continue;
            }

            if (FindRowIndex(table, rowId) >= 0)
            {
                if (!variantDelta!.DeletedBaseRowIds.Contains(rowId))
                {
                    variantDelta.DeletedBaseRowIds.Add(rowId);
                }

                RemoveVariantCellOverridesForRow(variantDelta, rowId);
                deletedRowIds.Add(rowId);
                continue;
            }

            errors.Add(new { rowId, error = $"Row not found in variant '{variantId}': {rowId}." });
            continue;
        }

        if (deletedRowIds.Count > 0)
        {
            NormalizeEditableSystemTablesIfNeeded(project, table, variantId);
            SaveActiveProjectAndNotify(project);
        }

        resultJson = JsonSerializer.Serialize(new
        {
            variantId,
            deletedCount = deletedRowIds.Count,
            deletedRowIds,
            errors,
        });
        return true;
    }

    private static void NormalizeEditableSystemTablesIfNeeded(DocProject project, DocTable table, int variantId)
    {
        if (variantId != DocTableVariant.BaseVariantId)
        {
            return;
        }

        if (!DocSystemTableRules.IsSystemTable(table))
        {
            return;
        }

        if (!string.Equals(table.SystemKey, DocSystemTableKeys.Packages, StringComparison.Ordinal) &&
            !string.Equals(table.SystemKey, DocSystemTableKeys.Exports, StringComparison.Ordinal))
        {
            return;
        }

        DocSystemTableSynchronizer.NormalizeEditableTables(project);
    }

    private bool ToolTableQuery(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string tableId = GetArgString(args, "tableId");
        if (string.IsNullOrWhiteSpace(tableId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing tableId." });
            return true;
        }

        var table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table not found: {tableId}." });
            return true;
        }

        if (!TryReadOptionalVariantIdArg(args, "variantId", DocTableVariant.BaseVariantId, out int variantId, out string variantError))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = variantError });
            return true;
        }

        if (!HasTableVariant(table, variantId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Variant '{variantId}' not found." });
            return true;
        }

        int offset = GetArgInt(args, "offset", 0);
        int limit = GetArgInt(args, "limit", 200);

        if (!TryBuildQueryRowsForVariant(project, table, variantId, offset, limit, out List<object> rows, out string queryError))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = queryError });
            return true;
        }

        resultJson = JsonSerializer.Serialize(new { variantId, rows });
        return true;
    }

    private bool ToolTableQueryBatch(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("queries", out var queries) ||
            queries.ValueKind != JsonValueKind.Array)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing queries array." });
            return true;
        }

        var results = new List<object>();
        foreach (var query in queries.EnumerateArray())
        {
            if (query.ValueKind != JsonValueKind.Object)
            {
                    results.Add(new
                    {
                        tableId = "",
                        variantId = DocTableVariant.BaseVariantId,
                        rows = Array.Empty<object>(),
                        error = "Invalid query entry: expected object.",
                    });
                continue;
            }

            string tableId = GetStringOrEmpty(query, "tableId");
            if (string.IsNullOrWhiteSpace(tableId))
            {
                    results.Add(new
                    {
                        tableId = "",
                        variantId = DocTableVariant.BaseVariantId,
                        rows = Array.Empty<object>(),
                        error = "Missing tableId.",
                    });
                continue;
            }

            if (!TryReadOptionalVariantIdArg(query, "variantId", DocTableVariant.BaseVariantId, out int variantId, out string variantError))
            {
                results.Add(new
                {
                    tableId,
                    variantId = DocTableVariant.BaseVariantId,
                    rows = Array.Empty<object>(),
                    error = variantError,
                });
                continue;
            }

            var table = FindTable(project, tableId);
            if (table == null)
            {
                results.Add(new
                {
                    tableId,
                    variantId,
                    rows = Array.Empty<object>(),
                    error = $"Table not found: {tableId}.",
                });
                continue;
            }

            if (!HasTableVariant(table, variantId))
            {
                results.Add(new
                {
                    tableId,
                    variantId,
                    rows = Array.Empty<object>(),
                    error = $"Variant '{variantId}' not found.",
                });
                continue;
            }

            int offset = GetArgInt(query, "offset", 0);
            int limit = GetArgInt(query, "limit", 200);
            if (!TryBuildQueryRowsForVariant(project, table, variantId, offset, limit, out List<object> rows, out string queryError))
            {
                results.Add(new
                {
                    tableId,
                    variantId,
                    rows = Array.Empty<object>(),
                    error = queryError,
                });
                continue;
            }

            results.Add(new
            {
                tableId,
                variantId,
                rows,
            });
        }

        resultJson = JsonSerializer.Serialize(new { results });
        return true;
    }

    private bool ToolExport(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        string path = GetArgString(args, "path");
        string generatedDir = GetArgString(args, "generatedDir");
        string binPath = GetArgString(args, "binPath");
        string livePath = GetArgString(args, "livePath");
        bool noManifest = GetArgBool(args, "noManifest", false);
        bool noLive = GetArgBool(args, "noLive", false);

        string dbRoot;
        string? gameRoot = null;
        if (!string.IsNullOrWhiteSpace(path))
        {
            string resolvedPath = ResolvePathWithinWorkspace(path);
            dbRoot = DocProjectPaths.ResolveDbRootFromPath(resolvedPath, allowCreate: false, out gameRoot);
            if (string.IsNullOrWhiteSpace(binPath))
            {
                binPath = DocProjectPaths.ResolveDefaultBinaryPath(dbRoot, gameRoot);
            }
        }
        else
        {
            TrySyncActiveDbRootFromUi();
            if (string.IsNullOrWhiteSpace(_activeDbRoot))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = "No active project. Call derpdoc.project.open first." });
                return true;
            }

            dbRoot = _activeDbRoot;
            if (DocProjectPaths.TryGetGameRootFromDbRoot(dbRoot, out var inferredGameRoot))
            {
                gameRoot = inferredGameRoot;
            }
            if (string.IsNullOrWhiteSpace(binPath))
            {
                binPath = DocProjectPaths.ResolveDefaultBinaryPath(dbRoot, gameRoot);
            }
        }

        if (string.IsNullOrWhiteSpace(livePath) && !noLive)
        {
            livePath = DocProjectPaths.ResolveDefaultLiveBinaryPath(dbRoot);
        }

        var options = new ExportPipelineOptions
        {
            GeneratedOutputDirectory = generatedDir ?? "",
            BinaryOutputPath = binPath,
            LiveBinaryOutputPath = noLive ? "" : livePath,
            WriteManifest = !noManifest,
        };

        var pipeline = new DocExportPipeline();
        var result = pipeline.ExportFromDirectory(dbRoot, options);

        if (result.HasErrors)
        {
            isError = true;
        }

        var diagnostics = new List<object>(result.Diagnostics.Count);
        for (int i = 0; i < result.Diagnostics.Count; i++)
        {
            var d = result.Diagnostics[i];
            diagnostics.Add(new { severity = d.Severity.ToString(), code = d.Code, message = d.Message, tableId = d.TableId, columnId = d.ColumnId });
        }

        resultJson = JsonSerializer.Serialize(new
        {
            binPath = options.BinaryOutputPath,
            livePath = options.LiveBinaryOutputPath,
            diagnostics
        });
        return true;
    }

    private bool ToolNanobananaGenerate(JsonElement args, out string resultJson, out bool isError)
    {
        return ToolNanobananaImageRequest(
            args,
            operationName: "generate",
            endpointPath: NanobananaGenerateEndpointPath,
            out resultJson,
            out isError);
    }

    private bool ToolNanobananaEdit(JsonElement args, out string resultJson, out bool isError)
    {
        return ToolNanobananaImageRequest(
            args,
            operationName: "edit",
            endpointPath: NanobananaEditEndpointPath,
            out resultJson,
            out isError);
    }

    private bool ToolNanobananaImageRequest(
        JsonElement args,
        string operationName,
        string endpointPath,
        out string resultJson,
        out bool isError)
    {
        resultJson = "{}";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("request", out JsonElement requestPayload) ||
            requestPayload.ValueKind != JsonValueKind.Object)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing request object." });
            return true;
        }

        if (!TryReadNanobananaConfiguration(out string apiBaseUrl, out string apiKey, out string configurationError))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = configurationError });
            return true;
        }

        if (!TryResolveActiveAssetsRoot(out string assetsRoot, out string assetsRootError))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = assetsRootError });
            return true;
        }

        if (!TryBuildNanobananaRequestJson(operationName, requestPayload, out string requestJson, out string requestError))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = requestError });
            return true;
        }

        if (!_nanobananaClient.TryInvoke(
                apiBaseUrl,
                endpointPath,
                apiKey,
                requestJson,
                out byte[] imageBytes,
                out string responseJson,
                out string apiError))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = apiError });
            return true;
        }

        string outputFolder = GetArgString(args, "outputFolder");
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            outputFolder = NanobananaDefaultOutputFolder;
        }

        string outputName = GetArgString(args, "outputName");
        bool hasExplicitOutputName = !string.IsNullOrWhiteSpace(outputName);
        if (hasExplicitOutputName)
        {
            outputName = EnsurePngExtension(outputName);
        }
        else
        {
            outputName = BuildNanobananaAutoFileName(requestPayload);
        }

        if (!TryNormalizeRelativeAssetPath(outputFolder, out string normalizedOutputFolder))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new
            {
                error = "outputFolder must be a valid relative path under Assets.",
            });
            return true;
        }

        if (!TryNormalizeRelativeAssetPath(outputName, out string normalizedOutputName))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new
            {
                error = "outputName must be a valid relative file path.",
            });
            return true;
        }

        string outputRelativePath = normalizedOutputFolder + "/" + normalizedOutputName;
        if (!TryNormalizeRelativeAssetPath(outputRelativePath, out outputRelativePath))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new
            {
                error = "Output path must remain within Assets and cannot contain '..'.",
            });
            return true;
        }

        string fullAssetsRoot = Path.GetFullPath(assetsRoot);
        string fullAssetsRootPrefix = fullAssetsRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullAssetsRoot
            : fullAssetsRoot + Path.DirectorySeparatorChar;
        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        string outputAbsolutePath = Path.GetFullPath(Path.Combine(fullAssetsRoot, outputRelativePath));
        if (!outputAbsolutePath.StartsWith(fullAssetsRootPrefix, pathComparison))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new
            {
                error = "Output path resolved outside Assets.",
            });
            return true;
        }

        bool overwroteExisting = hasExplicitOutputName && File.Exists(outputAbsolutePath);
        string outputDirectory = Path.GetDirectoryName(outputAbsolutePath) ?? fullAssetsRoot;
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllBytes(outputAbsolutePath, imageBytes);

        string assetPath = Path.GetRelativePath(fullAssetsRoot, outputAbsolutePath).Replace('\\', '/');

        string tableId = GetArgString(args, "tableId");
        string rowId = GetArgString(args, "rowId");
        string columnId = GetArgString(args, "columnId");
        bool assignmentRequested = !string.IsNullOrWhiteSpace(tableId) ||
                                   !string.IsNullOrWhiteSpace(rowId) ||
                                   !string.IsNullOrWhiteSpace(columnId);

        int variantId = DocTableVariant.BaseVariantId;
        bool rowUpdated = false;
        if (assignmentRequested)
        {
            if (string.IsNullOrWhiteSpace(tableId) ||
                string.IsNullOrWhiteSpace(rowId) ||
                string.IsNullOrWhiteSpace(columnId))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new
                {
                    error = "tableId, rowId, and columnId are all required when assigning image output to a cell.",
                    assetPath,
                });
                return true;
            }

            if (!TryReadOptionalVariantIdArg(args, "variantId", DocTableVariant.BaseVariantId, out variantId, out string variantError))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new
                {
                    error = variantError,
                    assetPath,
                });
                return true;
            }

            if (!TryAssignTextureAssetCell(project, tableId, rowId, columnId, variantId, assetPath, out string assignmentError))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new
                {
                    error = assignmentError,
                    assetPath,
                    variantId,
                });
                return true;
            }

            rowUpdated = true;
            SaveActiveProjectAndNotify(project);
        }

        resultJson = JsonSerializer.Serialize(new
        {
            operation = operationName,
            assetPath,
            overwroteExisting,
            rowUpdated,
            variantId,
            responseJson,
        });
        return true;
    }

    private bool ToolElevenLabsGenerate(JsonElement args, out string resultJson, out bool isError)
    {
        return ToolElevenLabsAudioRequest(
            args,
            operationName: "generate",
            out resultJson,
            out isError);
    }

    private bool ToolElevenLabsEdit(JsonElement args, out string resultJson, out bool isError)
    {
        return ToolElevenLabsAudioRequest(
            args,
            operationName: "edit",
            out resultJson,
            out isError);
    }

    private bool ToolElevenLabsAudioRequest(
        JsonElement args,
        string operationName,
        out string resultJson,
        out bool isError)
    {
        resultJson = "{}";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("request", out JsonElement requestPayload) ||
            requestPayload.ValueKind != JsonValueKind.Object)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing request object." });
            return true;
        }

        if (!TryReadElevenLabsConfiguration(out string apiBaseUrl, out string apiKey, out string configurationError))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = configurationError });
            return true;
        }

        if (!TryResolveActiveAssetsRoot(out string assetsRoot, out string assetsRootError))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = assetsRootError });
            return true;
        }

        byte[] audioBytes;
        string responseText;
        if (string.Equals(operationName, "generate", StringComparison.Ordinal))
        {
            if (!TryBuildElevenLabsTextToSpeechRequest(
                    requestPayload,
                    out string voiceId,
                    out string outputFormat,
                    out bool? enableLogging,
                    out string requestJson,
                    out string requestError))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = requestError });
                return true;
            }

            if (!_elevenLabsClient.TryTextToSpeech(
                    apiBaseUrl,
                    apiKey,
                    voiceId,
                    outputFormat,
                    enableLogging,
                    requestJson,
                    out audioBytes,
                    out responseText,
                    out string apiError))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = apiError });
                return true;
            }
        }
        else if (string.Equals(operationName, "edit", StringComparison.Ordinal))
        {
            if (!TryBuildElevenLabsSpeechToSpeechRequest(
                    requestPayload,
                    out string voiceId,
                    out string outputFormat,
                    out bool? enableLogging,
                    out string requestJson,
                    out byte[] inputAudioBytes,
                    out string inputAudioFileName,
                    out string inputAudioMimeType,
                    out string requestError))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = requestError });
                return true;
            }

            if (!_elevenLabsClient.TrySpeechToSpeech(
                    apiBaseUrl,
                    apiKey,
                    voiceId,
                    outputFormat,
                    enableLogging,
                    requestJson,
                    inputAudioBytes,
                    inputAudioFileName,
                    inputAudioMimeType,
                    out audioBytes,
                    out responseText,
                    out string apiError))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = apiError });
                return true;
            }
        }
        else
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Unsupported ElevenLabs operation: " + operationName + "." });
            return true;
        }

        string outputFolder = GetArgString(args, "outputFolder");
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            outputFolder = ElevenLabsDefaultOutputFolder;
        }

        string outputName = GetArgString(args, "outputName");
        bool hasExplicitOutputName = !string.IsNullOrWhiteSpace(outputName);
        if (hasExplicitOutputName)
        {
            outputName = EnsureFileExtension(outputName, ".mp3");
        }
        else
        {
            outputName = BuildElevenLabsAutoFileName(requestPayload);
        }

        if (!TryNormalizeRelativeAssetPath(outputFolder, out string normalizedOutputFolder))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new
            {
                error = "outputFolder must be a valid relative path under Assets.",
            });
            return true;
        }

        if (!TryNormalizeRelativeAssetPath(outputName, out string normalizedOutputName))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new
            {
                error = "outputName must be a valid relative file path.",
            });
            return true;
        }

        string outputRelativePath = normalizedOutputFolder + "/" + normalizedOutputName;
        if (!TryNormalizeRelativeAssetPath(outputRelativePath, out outputRelativePath))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new
            {
                error = "Output path must remain within Assets and cannot contain '..'.",
            });
            return true;
        }

        string fullAssetsRoot = Path.GetFullPath(assetsRoot);
        string fullAssetsRootPrefix = fullAssetsRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullAssetsRoot
            : fullAssetsRoot + Path.DirectorySeparatorChar;
        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        string outputAbsolutePath = Path.GetFullPath(Path.Combine(fullAssetsRoot, outputRelativePath));
        if (!outputAbsolutePath.StartsWith(fullAssetsRootPrefix, pathComparison))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new
            {
                error = "Output path resolved outside Assets.",
            });
            return true;
        }

        bool overwroteExisting = hasExplicitOutputName && File.Exists(outputAbsolutePath);
        string outputDirectory = Path.GetDirectoryName(outputAbsolutePath) ?? fullAssetsRoot;
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllBytes(outputAbsolutePath, audioBytes);

        string assetPath = Path.GetRelativePath(fullAssetsRoot, outputAbsolutePath).Replace('\\', '/');

        string tableId = GetArgString(args, "tableId");
        string rowId = GetArgString(args, "rowId");
        string columnId = GetArgString(args, "columnId");
        bool assignmentRequested = !string.IsNullOrWhiteSpace(tableId) ||
                                   !string.IsNullOrWhiteSpace(rowId) ||
                                   !string.IsNullOrWhiteSpace(columnId);

        int variantId = DocTableVariant.BaseVariantId;
        bool rowUpdated = false;
        if (assignmentRequested)
        {
            if (string.IsNullOrWhiteSpace(tableId) ||
                string.IsNullOrWhiteSpace(rowId) ||
                string.IsNullOrWhiteSpace(columnId))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new
                {
                    error = "tableId, rowId, and columnId are all required when assigning audio output to a cell.",
                    assetPath,
                });
                return true;
            }

            if (!TryReadOptionalVariantIdArg(args, "variantId", DocTableVariant.BaseVariantId, out variantId, out string variantError))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new
                {
                    error = variantError,
                    assetPath,
                });
                return true;
            }

            if (!TryAssignAudioAssetCell(project, tableId, rowId, columnId, variantId, assetPath, out string assignmentError))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new
                {
                    error = assignmentError,
                    assetPath,
                    variantId,
                });
                return true;
            }

            rowUpdated = true;
            SaveActiveProjectAndNotify(project);
        }

        resultJson = JsonSerializer.Serialize(new
        {
            operation = operationName,
            assetPath,
            overwroteExisting,
            rowUpdated,
            variantId,
            responseText,
        });
        return true;
    }

    private static void ApplyCells(DocTable table, DocRow row, JsonElement cells)
    {
        foreach (var prop in cells.EnumerateObject())
        {
            string columnId = prop.Name;
            var col = FindColumn(table, columnId);
            if (col == null)
            {
                continue;
            }

            var v = prop.Value;
            if (TryConvertToolCellValue(col, v, out var convertedCellValue))
            {
                row.SetCell(columnId, DocCellValueNormalizer.NormalizeForColumn(col, convertedCellValue));
            }
        }
    }

    private static bool TryConvertToolCellValue(DocColumn column, JsonElement toolValue, out DocCellValue cellValue)
    {
        string columnTypeId = DocColumnTypeIdMapper.Resolve(column.ColumnTypeId, column.Kind);
        if (!DocColumnTypeIdMapper.IsBuiltIn(columnTypeId))
        {
            if (ColumnCellCodecProviderRegistry.TryReadMcpCellValue(columnTypeId, column, toolValue, out cellValue))
            {
                return true;
            }

            cellValue = ConvertNonBuiltInToolCellFallback(toolValue);
            return true;
        }

        switch (column.Kind)
        {
            case DocColumnKind.Checkbox:
                if (toolValue.ValueKind == JsonValueKind.True || toolValue.ValueKind == JsonValueKind.False)
                {
                    cellValue = DocCellValue.Bool(toolValue.GetBoolean());
                    return true;
                }
                break;
            case DocColumnKind.Number:
            case DocColumnKind.Formula:
                if (toolValue.ValueKind == JsonValueKind.Number)
                {
                    cellValue = DocCellValue.Number(toolValue.GetDouble());
                    return true;
                }

                if (toolValue.ValueKind == JsonValueKind.String &&
                    double.TryParse(toolValue.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    cellValue = DocCellValue.Number(parsed);
                    return true;
                }
                break;
            case DocColumnKind.Vec2:
                return TryParseVectorToolCellValue(toolValue, dimension: 2, out cellValue);
            case DocColumnKind.Vec3:
                return TryParseVectorToolCellValue(toolValue, dimension: 3, out cellValue);
            case DocColumnKind.Vec4:
                return TryParseVectorToolCellValue(toolValue, dimension: 4, out cellValue);
            case DocColumnKind.Color:
                return TryParseColorToolCellValue(toolValue, out cellValue);
            default:
                if (toolValue.ValueKind == JsonValueKind.String)
                {
                    cellValue = DocCellValue.Text(toolValue.GetString() ?? "");
                    return true;
                }

                if (toolValue.ValueKind == JsonValueKind.Number)
                {
                    cellValue = DocCellValue.Text(toolValue.GetDouble().ToString(CultureInfo.InvariantCulture));
                    return true;
                }

                if (toolValue.ValueKind == JsonValueKind.True || toolValue.ValueKind == JsonValueKind.False)
                {
                    cellValue = DocCellValue.Text(toolValue.GetBoolean() ? "true" : "false");
                    return true;
                }
                break;
        }

        cellValue = default;
        return false;
    }

    private static DocCellValue ConvertNonBuiltInToolCellFallback(JsonElement toolValue)
    {
        return toolValue.ValueKind switch
        {
            JsonValueKind.String => DocCellValue.Text(toolValue.GetString() ?? ""),
            JsonValueKind.Number => DocCellValue.Text(toolValue.GetDouble().ToString(CultureInfo.InvariantCulture)),
            JsonValueKind.True => DocCellValue.Text("true"),
            JsonValueKind.False => DocCellValue.Text("false"),
            JsonValueKind.Object => DocCellValue.Text(toolValue.GetRawText()),
            JsonValueKind.Array => DocCellValue.Text(toolValue.GetRawText()),
            JsonValueKind.Null => DocCellValue.Text(""),
            JsonValueKind.Undefined => DocCellValue.Text(""),
            _ => DocCellValue.Text(toolValue.ToString()),
        };
    }

    private static List<object> BuildQueryRows(DocTable table, int offset, int limit)
    {
        offset = Math.Max(0, offset);
        limit = Math.Max(0, limit);

        int end = Math.Min(table.Rows.Count, offset + limit);
        var rows = new List<object>(Math.Max(0, end - offset));
        for (int rowIndex = offset; rowIndex < end; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var cells = new Dictionary<string, object?>(table.Columns.Count);
            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                var column = table.Columns[columnIndex];
                var cell = row.GetCell(column);
                cells[column.Id] = FormatToolCellValue(column, cell);
            }

            rows.Add(new { id = row.Id, cells });
        }

        return rows;
    }

    private static object? FormatToolCellValue(DocColumn column, DocCellValue cell)
    {
        string columnTypeId = DocColumnTypeIdMapper.Resolve(column.ColumnTypeId, column.Kind);
        if (!DocColumnTypeIdMapper.IsBuiltIn(columnTypeId))
        {
            if (ColumnCellCodecProviderRegistry.TryFormatMcpCellValue(columnTypeId, column, cell, out var toolValue))
            {
                return toolValue;
            }

            return cell.StringValue ?? "";
        }

        switch (column.Kind)
        {
            case DocColumnKind.Checkbox:
                return cell.BoolValue;
            case DocColumnKind.Number:
            case DocColumnKind.Formula:
                return cell.NumberValue;
            case DocColumnKind.Vec2:
                return new
                {
                    x = cell.XValue,
                    y = cell.YValue,
                };
            case DocColumnKind.Vec3:
                return new
                {
                    x = cell.XValue,
                    y = cell.YValue,
                    z = cell.ZValue,
                };
            case DocColumnKind.Vec4:
                return new
                {
                    x = cell.XValue,
                    y = cell.YValue,
                    z = cell.ZValue,
                    w = cell.WValue,
                };
            case DocColumnKind.Color:
                return new
                {
                    r = cell.XValue,
                    g = cell.YValue,
                    b = cell.ZValue,
                    a = cell.WValue,
                };
            default:
                return cell.StringValue ?? "";
        }
    }

    private static bool TryParseVectorToolCellValue(JsonElement toolValue, int dimension, out DocCellValue cellValue)
    {
        cellValue = default;
        if (dimension < 2 || dimension > 4)
        {
            return false;
        }

        double xValue = 0;
        double yValue = 0;
        double zValue = 0;
        double wValue = 0;

        if (toolValue.ValueKind == JsonValueKind.Object)
        {
            bool hasX = TryGetNamedToolNumber(toolValue, "x", out xValue);
            bool hasY = TryGetNamedToolNumber(toolValue, "y", out yValue);
            bool hasZ = TryGetNamedToolNumber(toolValue, "z", out zValue);
            bool hasW = TryGetNamedToolNumber(toolValue, "w", out wValue);

            if (!hasX || !hasY || (dimension >= 3 && !hasZ) || (dimension >= 4 && !hasW))
            {
                return false;
            }
        }
        else if (toolValue.ValueKind == JsonValueKind.Array)
        {
            JsonElement.ArrayEnumerator enumerator = toolValue.EnumerateArray();
            var components = new List<double>(4);
            while (enumerator.MoveNext())
            {
                if (!TryGetNumericToolValue(enumerator.Current, out double componentValue))
                {
                    return false;
                }

                components.Add(componentValue);
            }

            if (components.Count < dimension)
            {
                return false;
            }

            xValue = components[0];
            yValue = components[1];
            zValue = dimension >= 3 ? components[2] : 0;
            wValue = dimension >= 4 ? components[3] : 0;
        }
        else
        {
            return false;
        }

        cellValue = dimension switch
        {
            2 => DocCellValue.Vec2(xValue, yValue),
            3 => DocCellValue.Vec3(xValue, yValue, zValue),
            _ => DocCellValue.Vec4(xValue, yValue, zValue, wValue),
        };
        return true;
    }

    private static bool TryParseColorToolCellValue(JsonElement toolValue, out DocCellValue cellValue)
    {
        cellValue = default;
        double red = 1;
        double green = 1;
        double blue = 1;
        double alpha = 1;

        if (toolValue.ValueKind == JsonValueKind.Object)
        {
            if (!TryGetNamedToolNumber(toolValue, "r", out red) ||
                !TryGetNamedToolNumber(toolValue, "g", out green) ||
                !TryGetNamedToolNumber(toolValue, "b", out blue))
            {
                return false;
            }

            if (TryGetNamedToolNumber(toolValue, "a", out double parsedAlpha))
            {
                alpha = parsedAlpha;
            }
        }
        else if (toolValue.ValueKind == JsonValueKind.Array)
        {
            JsonElement.ArrayEnumerator enumerator = toolValue.EnumerateArray();
            var components = new List<double>(4);
            while (enumerator.MoveNext())
            {
                if (!TryGetNumericToolValue(enumerator.Current, out double componentValue))
                {
                    return false;
                }

                components.Add(componentValue);
            }

            if (components.Count < 3)
            {
                return false;
            }

            red = components[0];
            green = components[1];
            blue = components[2];
            if (components.Count >= 4)
            {
                alpha = components[3];
            }
        }
        else
        {
            return false;
        }

        cellValue = DocCellValue.Color(red, green, blue, alpha);
        return true;
    }

    private static bool TryGetNamedToolNumber(JsonElement objectValue, string propertyName, out double value)
    {
        value = 0;
        if (!objectValue.TryGetProperty(propertyName, out JsonElement propertyValue))
        {
            return false;
        }

        return TryGetNumericToolValue(propertyValue, out value);
    }

    private static bool TryGetNumericToolValue(JsonElement valueElement, out double numericValue)
    {
        numericValue = 0;
        if (valueElement.ValueKind == JsonValueKind.Number)
        {
            numericValue = valueElement.GetDouble();
            return true;
        }

        if (valueElement.ValueKind == JsonValueKind.String &&
            double.TryParse(valueElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            numericValue = parsed;
            return true;
        }

        return false;
    }

    // ── View handlers ──

    private bool ToolViewList(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        { isError = true; return true; }

        string tableId = GetArgString(args, "tableId");
        var table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table '{tableId}' not found." });
            return true;
        }

        var views = new List<object>(table.Views.Count);
        for (int i = 0; i < table.Views.Count; i++)
        {
            var v = table.Views[i];
            views.Add(new
            {
                id = v.Id,
                name = v.Name,
                type = v.Type.ToString(),
                customRendererId = v.CustomRendererId,
                visibleColumnIds = v.VisibleColumnIds,
                filters = v.Filters.Select(f => new { columnId = f.ColumnId, op = f.Op.ToString(), value = f.Value }),
                sorts = v.Sorts.Select(s => new { columnId = s.ColumnId, descending = s.Descending }),
                groupByColumnId = v.GroupByColumnId,
                calendarDateColumnId = v.CalendarDateColumnId,
                chartKind = v.ChartKind?.ToString(),
                chartCategoryColumnId = v.ChartCategoryColumnId,
                chartValueColumnId = v.ChartValueColumnId,
            });
        }

        resultJson = JsonSerializer.Serialize(new { views });
        return true;
    }

    private bool ToolViewCreate(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        { isError = true; return true; }

        string tableId = GetArgString(args, "tableId");
        var table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table '{tableId}' not found." });
            return true;
        }

        string name = GetArgString(args, "name");
        string typeStr = GetArgString(args, "type");
        var viewType = typeStr.ToLowerInvariant() switch
        {
            "board" => DocViewType.Board,
            "calendar" => DocViewType.Calendar,
            "chart" => DocViewType.Chart,
            "custom" => DocViewType.Custom,
            _ => DocViewType.Grid,
        };

        var view = new DocView
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"{viewType} view" : name,
            Type = viewType,
            CustomRendererId = GetArgStringOrNull(args, "customRendererId"),
            GroupByColumnId = GetArgStringOrNull(args, "groupByColumnId"),
            CalendarDateColumnId = GetArgStringOrNull(args, "calendarDateColumnId"),
            ChartCategoryColumnId = GetArgStringOrNull(args, "chartCategoryColumnId"),
            ChartValueColumnId = GetArgStringOrNull(args, "chartValueColumnId"),
        };
        if (view.Type != DocViewType.Custom)
        {
            view.CustomRendererId = null;
        }

        string chartKindStr = GetArgString(args, "chartKind");
        if (!string.IsNullOrWhiteSpace(chartKindStr) && Enum.TryParse<DocChartKind>(chartKindStr, true, out var ck))
            view.ChartKind = ck;

        // VisibleColumnIds
        if (args.TryGetProperty("visibleColumnIds", out var visCols) && visCols.ValueKind == JsonValueKind.Array)
        {
            view.VisibleColumnIds = new List<string>();
            foreach (var el in visCols.EnumerateArray())
            {
                string? s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s)) view.VisibleColumnIds.Add(s);
            }
        }

        // Filters
        ParseFiltersFromArgs(args, view);

        // Sorts
        ParseSortsFromArgs(args, view);

        table.Views.Add(view);
        SaveActiveProjectAndNotify(project);
        resultJson = JsonSerializer.Serialize(new
        {
            viewId = view.Id,
            name = view.Name,
            type = view.Type.ToString(),
            customRendererId = view.CustomRendererId
        });
        return true;
    }

    private bool ToolViewUpdate(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        { isError = true; return true; }

        string tableId = GetArgString(args, "tableId");
        string viewId = GetArgString(args, "viewId");
        var table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table '{tableId}' not found." });
            return true;
        }

        DocView? view = null;
        for (int i = 0; i < table.Views.Count; i++)
        {
            if (string.Equals(table.Views[i].Id, viewId, StringComparison.Ordinal))
            { view = table.Views[i]; break; }
        }
        if (view == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"View '{viewId}' not found." });
            return true;
        }

        // Apply updates
        string name = GetArgString(args, "name");
        if (!string.IsNullOrWhiteSpace(name)) view.Name = name;

        string typeStr = GetArgString(args, "type");
        if (!string.IsNullOrWhiteSpace(typeStr))
        {
            view.Type = typeStr.ToLowerInvariant() switch
            {
                "board" => DocViewType.Board,
                "calendar" => DocViewType.Calendar,
                "chart" => DocViewType.Chart,
                "custom" => DocViewType.Custom,
                _ => DocViewType.Grid,
            };
        }

        if (args.TryGetProperty("customRendererId", out var customRendererIdProp))
        {
            view.CustomRendererId = customRendererIdProp.ValueKind == JsonValueKind.String ? customRendererIdProp.GetString() : null;
        }
        if (view.Type != DocViewType.Custom)
        {
            view.CustomRendererId = null;
        }

        if (args.TryGetProperty("groupByColumnId", out var gbProp))
            view.GroupByColumnId = gbProp.ValueKind == JsonValueKind.String ? gbProp.GetString() : null;
        if (args.TryGetProperty("calendarDateColumnId", out var cdProp))
            view.CalendarDateColumnId = cdProp.ValueKind == JsonValueKind.String ? cdProp.GetString() : null;
        if (args.TryGetProperty("chartCategoryColumnId", out var ccProp))
            view.ChartCategoryColumnId = ccProp.ValueKind == JsonValueKind.String ? ccProp.GetString() : null;
        if (args.TryGetProperty("chartValueColumnId", out var cvProp))
            view.ChartValueColumnId = cvProp.ValueKind == JsonValueKind.String ? cvProp.GetString() : null;
        string chartKindStr2 = GetArgString(args, "chartKind");
        if (!string.IsNullOrWhiteSpace(chartKindStr2) && Enum.TryParse<DocChartKind>(chartKindStr2, true, out var ck2))
            view.ChartKind = ck2;

        // VisibleColumnIds
        if (args.TryGetProperty("visibleColumnIds", out var visCols) && visCols.ValueKind == JsonValueKind.Array)
        {
            var ids = new List<string>();
            foreach (var el in visCols.EnumerateArray())
            {
                string? s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s)) ids.Add(s);
            }
            view.VisibleColumnIds = ids.Count > 0 ? ids : null;
        }

        // Filters
        ParseFiltersFromArgs(args, view);

        // Sorts
        ParseSortsFromArgs(args, view);

        SaveActiveProjectAndNotify(project);
        resultJson = JsonSerializer.Serialize(new { viewId = view.Id, updated = true });
        return true;
    }

    private bool ToolViewDelete(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        { isError = true; return true; }

        string tableId = GetArgString(args, "tableId");
        string viewId = GetArgString(args, "viewId");
        var table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table '{tableId}' not found." });
            return true;
        }

        bool deleted = false;
        for (int i = 0; i < table.Views.Count; i++)
        {
            if (string.Equals(table.Views[i].Id, viewId, StringComparison.Ordinal))
            {
                table.Views.RemoveAt(i);
                deleted = true;
                break;
            }
        }

        if (!deleted)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"View '{viewId}' not found." });
            return true;
        }

        SaveActiveProjectAndNotify(project);
        resultJson = JsonSerializer.Serialize(new { deleted = true });
        return true;
    }

    private bool ToolNodeGraphEnsure(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string tableId = GetArgString(args, "tableId");
        if (string.IsNullOrWhiteSpace(tableId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'tableId'." });
            return true;
        }

        DocTable? table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table '{tableId}' not found." });
            return true;
        }

        string requestedViewId = GetArgString(args, "viewId");
        string requestedViewName = GetArgString(args, "viewName");
        bool createdView;
        DocView? view = ResolveOrCreateNodeGraphView(table, requestedViewId, requestedViewName, out createdView);
        if (view == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"View '{requestedViewId}' not found on table '{tableId}'." });
            return true;
        }

        bool updatedView = false;
        if (view.Type != DocViewType.Custom)
        {
            view.Type = DocViewType.Custom;
            updatedView = true;
        }

        if (!string.Equals(view.CustomRendererId, NodeGraphRendererId, StringComparison.Ordinal))
        {
            view.CustomRendererId = NodeGraphRendererId;
            updatedView = true;
        }

        if (!string.IsNullOrWhiteSpace(requestedViewName) &&
            !string.Equals(view.Name, requestedViewName, StringComparison.Ordinal))
        {
            view.Name = requestedViewName;
            updatedView = true;
        }

        NodeGraphViewSettingsPayload settings = ReadNodeGraphViewSettings(project, table, view);
        bool scaffolded = EnsureNodeGraphSchemaScaffold(project, table, settings, out NodeGraphResolvedSchema schema);
        bool layoutUpdated = EnsureNodeGraphTypeLayoutsContainActiveSchemaColumns(table, schema, settings);
        settings.TypeLayouts.Sort(static (left, right) => string.Compare(left.TypeName, right.TypeName, StringComparison.OrdinalIgnoreCase));

        if (createdView || updatedView || scaffolded || layoutUpdated)
        {
            WriteNodeGraphViewSettings(project, table, view, settings);
            SaveActiveProjectAndNotify(project);
        }

        resultJson = JsonSerializer.Serialize(new
        {
            tableId = table.Id,
            viewId = view.Id,
            createdView,
            updatedView,
            scaffolded = scaffolded || layoutUpdated,
            schema = new
            {
                typeColumnId = schema.TypeColumn?.Id ?? "",
                positionColumnId = schema.PositionColumn?.Id ?? "",
                titleColumnId = schema.TitleColumn?.Id ?? "",
                executionOutputColumnId = schema.ExecutionOutputColumn?.Id ?? "",
                edgeSubtableColumnId = schema.EdgeSubtableColumn?.Id ?? "",
                edgeTableId = schema.EdgeTable?.Id ?? "",
            }
        });
        return true;
    }

    private bool ToolNodeGraphGet(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string tableId = GetArgString(args, "tableId");
        if (string.IsNullOrWhiteSpace(tableId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'tableId'." });
            return true;
        }

        DocTable? table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table '{tableId}' not found." });
            return true;
        }

        string requestedViewId = GetArgString(args, "viewId");
        DocView? view = ResolveExistingNodeGraphView(table, requestedViewId);
        if (view == null)
        {
            string missingViewMessage = string.IsNullOrWhiteSpace(requestedViewId)
                ? $"No node graph view found on table '{tableId}'."
                : $"Node graph view '{requestedViewId}' not found on table '{tableId}'.";
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = missingViewMessage });
            return true;
        }

        NodeGraphViewSettingsPayload settings = ReadNodeGraphViewSettings(project, table, view);
        NodeGraphResolvedSchema schema = ResolveNodeGraphSchema(project, table, settings);
        bool layoutsChanged = EnsureNodeGraphTypeLayoutsContainActiveSchemaColumns(table, schema, settings);
        if (layoutsChanged)
        {
            WriteNodeGraphViewSettings(project, table, view, settings);
            SaveActiveProjectAndNotify(project);
        }

        resultJson = JsonSerializer.Serialize(new
        {
            tableId = table.Id,
            viewId = view.Id,
            viewName = view.Name,
            schema = BuildNodeGraphSchemaPayload(schema),
            settings = BuildNodeGraphSettingsPayload(table, schema, settings),
        });
        return true;
    }

    private bool ToolNodeGraphLayoutSet(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string tableId = GetArgString(args, "tableId");
        string viewId = GetArgString(args, "viewId");
        string typeName = GetArgString(args, "typeName");
        if (string.IsNullOrWhiteSpace(tableId) || string.IsNullOrWhiteSpace(viewId) || string.IsNullOrWhiteSpace(typeName))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing required fields: tableId, viewId, typeName." });
            return true;
        }

        DocTable? table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table '{tableId}' not found." });
            return true;
        }

        DocView? view = FindViewById(table, viewId);
        if (view == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"View '{viewId}' not found on table '{tableId}'." });
            return true;
        }

        if (view.Type != DocViewType.Custom || !string.Equals(view.CustomRendererId, NodeGraphRendererId, StringComparison.Ordinal))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"View '{viewId}' is not a node graph view (custom renderer '{NodeGraphRendererId}')." });
            return true;
        }

        NodeGraphViewSettingsPayload settings = ReadNodeGraphViewSettings(project, table, view);
        NodeGraphResolvedSchema schema = ResolveNodeGraphSchema(project, table, settings);
        if (!HasRequiredNodeGraphSchema(schema))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Node graph schema is incomplete. Call derpdoc.nodegraph.ensure first." });
            return true;
        }

        bool updated = false;
        NodeGraphTypeLayoutPayload typeLayout = GetOrCreateNodeGraphTypeLayout(settings, typeName.Trim());

        if (TryGetNamedToolNumber(args, "nodeWidth", out double parsedNodeWidth))
        {
            float clampedNodeWidth = ClampNodeGraphNodeWidth((float)parsedNodeWidth);
            if (Math.Abs(typeLayout.NodeWidth - clampedNodeWidth) > 0.001f)
            {
                typeLayout.NodeWidth = clampedNodeWidth;
                updated = true;
            }
        }

        bool replaceFields = GetArgBool(args, "replaceFields", false);
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("fields", out JsonElement fieldsElement))
        {
            if (fieldsElement.ValueKind != JsonValueKind.Array)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = "'fields' must be an array when provided." });
                return true;
            }

            var parsedFields = new List<NodeGraphFieldLayoutPayload>();
            var seenColumnIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement fieldElement in fieldsElement.EnumerateArray())
            {
                if (fieldElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string columnId = GetStringOrEmpty(fieldElement, "columnId");
                string mode = GetStringOrEmpty(fieldElement, "mode");
                if (string.IsNullOrWhiteSpace(columnId) || string.IsNullOrWhiteSpace(mode))
                {
                    continue;
                }

                if (!TryNormalizeNodeGraphFieldMode(mode, out string normalizedMode))
                {
                    isError = true;
                    resultJson = JsonSerializer.Serialize(new { error = $"Invalid field mode '{mode}'. Use Hidden|Setting|InputPin|OutputPin." });
                    return true;
                }

                if (FindColumn(table, columnId) == null)
                {
                    isError = true;
                    resultJson = JsonSerializer.Serialize(new { error = $"Column '{columnId}' not found on table '{tableId}'." });
                    return true;
                }

                if (!seenColumnIds.Add(columnId))
                {
                    continue;
                }

                parsedFields.Add(new NodeGraphFieldLayoutPayload
                {
                    ColumnId = columnId,
                    Mode = normalizedMode,
                });
            }

            if (replaceFields)
            {
                if (!AreNodeGraphFieldsEqual(typeLayout.Fields, parsedFields))
                {
                    typeLayout.Fields = parsedFields;
                    updated = true;
                }
            }
            else
            {
                for (int fieldIndex = 0; fieldIndex < parsedFields.Count; fieldIndex++)
                {
                    NodeGraphFieldLayoutPayload parsedField = parsedFields[fieldIndex];
                    int existingFieldIndex = FindNodeGraphFieldIndex(typeLayout.Fields, parsedField.ColumnId);
                    if (existingFieldIndex < 0)
                    {
                        typeLayout.Fields.Add(parsedField);
                        updated = true;
                        continue;
                    }

                    if (!string.Equals(typeLayout.Fields[existingFieldIndex].Mode, parsedField.Mode, StringComparison.Ordinal))
                    {
                        typeLayout.Fields[existingFieldIndex].Mode = parsedField.Mode;
                        updated = true;
                    }
                }
            }
        }

        bool layoutDefaultsUpdated = EnsureNodeGraphTypeLayoutsContainActiveSchemaColumns(table, schema, settings);
        settings.TypeLayouts.Sort(static (left, right) => string.Compare(left.TypeName, right.TypeName, StringComparison.OrdinalIgnoreCase));
        updated |= layoutDefaultsUpdated;

        if (updated)
        {
            WriteNodeGraphViewSettings(project, table, view, settings);
            SaveActiveProjectAndNotify(project);
        }

        resultJson = JsonSerializer.Serialize(new
        {
            tableId = table.Id,
            viewId = view.Id,
            typeName = typeLayout.TypeName,
            nodeWidth = typeLayout.NodeWidth,
            fieldCount = typeLayout.Fields.Count,
            updated,
        });
        return true;
    }

    // ── Document handlers ──

    private bool ToolDocumentList(out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        { isError = true; return true; }

        var documents = new List<object>(project.Documents.Count);
        for (int i = 0; i < project.Documents.Count; i++)
        {
            var d = project.Documents[i];
            int tableBlockCount = 0;
            for (int j = 0; j < d.Blocks.Count; j++)
            {
                if (d.Blocks[j].Type == DocBlockType.Table) tableBlockCount++;
            }
            documents.Add(new
            {
                id = d.Id,
                title = d.Title,
                fileName = d.FileName,
                folderId = d.FolderId,
                blockCount = d.Blocks.Count,
                tableBlockCount,
            });
        }

        resultJson = JsonSerializer.Serialize(new { documents });
        return true;
    }

    private bool ToolDocumentCreate(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string title = GetArgString(args, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            title = "Document " + (project.Documents.Count + 1).ToString(CultureInfo.InvariantCulture);
        }

        string fileName = GetArgString(args, "fileName");
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "document" + (project.Documents.Count + 1).ToString(CultureInfo.InvariantCulture);
        }
        fileName = MakeUniqueDocumentFileName(project, SanitizeDocumentFileStem(fileName));

        string? folderId = GetArgStringOrNull(args, "folderId");
        if (!string.IsNullOrWhiteSpace(folderId))
        {
            DocFolder? folder = FindFolder(project, folderId);
            if (folder == null || folder.Scope != DocFolderScope.Documents)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"Folder '{folderId}' not found or not Documents scope." });
                return true;
            }
        }

        string initialText = GetArgString(args, "initialText");

        var document = new DocDocument
        {
            Title = title,
            FileName = fileName,
            FolderId = string.IsNullOrWhiteSpace(folderId) ? null : folderId,
        };

        document.Blocks.Add(new DocBlock
        {
            Type = DocBlockType.Paragraph,
            Order = FractionalIndex.Initial(),
            Text = new RichText
            {
                PlainText = initialText,
            },
        });

        project.Documents.Add(document);
        SaveActiveProjectAndNotify(project);
        resultJson = JsonSerializer.Serialize(new { documentId = document.Id, fileName = document.FileName });
        return true;
    }

    private bool ToolDocumentUpdate(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string documentId = GetArgString(args, "documentId");
        if (string.IsNullOrWhiteSpace(documentId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'documentId'." });
            return true;
        }

        var document = FindDocument(project, documentId);
        if (document == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Document '{documentId}' not found." });
            return true;
        }

        bool updated = false;

        if (args.TryGetProperty("title", out var titleElement))
        {
            if (titleElement.ValueKind != JsonValueKind.String)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = "Invalid 'title'. Expected string." });
                return true;
            }

            string newTitle = titleElement.GetString() ?? "";
            if (!string.IsNullOrWhiteSpace(newTitle) && !string.Equals(document.Title, newTitle, StringComparison.Ordinal))
            {
                document.Title = newTitle;
                updated = true;
            }
        }

        if (args.TryGetProperty("folderId", out var folderElement))
        {
            string? folderId = folderElement.ValueKind switch
            {
                JsonValueKind.Null => "",
                JsonValueKind.String => folderElement.GetString(),
                _ => null,
            };

            if (folderId == null)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = "Invalid 'folderId'. Must be string or null." });
                return true;
            }

            if (!string.IsNullOrWhiteSpace(folderId))
            {
                DocFolder? folder = FindFolder(project, folderId);
                if (folder == null || folder.Scope != DocFolderScope.Documents)
                {
                    isError = true;
                    resultJson = JsonSerializer.Serialize(new { error = $"Folder '{folderId}' not found or not Documents scope." });
                    return true;
                }
            }

            string? nextFolderId = string.IsNullOrWhiteSpace(folderId) ? null : folderId;
            if (!string.Equals(document.FolderId, nextFolderId, StringComparison.Ordinal))
            {
                document.FolderId = nextFolderId;
                updated = true;
            }
        }

        if (updated)
        {
            SaveActiveProjectAndNotify(project);
        }

        resultJson = JsonSerializer.Serialize(new { documentId = document.Id, updated });
        return true;
    }

    private bool ToolDocumentDelete(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string documentId = GetArgString(args, "documentId");
        if (string.IsNullOrWhiteSpace(documentId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'documentId'." });
            return true;
        }

        int index = FindDocumentIndex(project, documentId);
        if (index < 0)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Document '{documentId}' not found." });
            return true;
        }

        project.Documents.RemoveAt(index);
        SaveActiveProjectAndNotify(project);
        resultJson = JsonSerializer.Serialize(new { deleted = true });
        return true;
    }

    private bool ToolDocumentFolderSet(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string documentId = GetArgString(args, "documentId");
        if (string.IsNullOrWhiteSpace(documentId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'documentId'." });
            return true;
        }

        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("folderId", out var folderElement))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'folderId'. Pass empty string to clear folder." });
            return true;
        }

        string? folderId = folderElement.ValueKind switch
        {
            JsonValueKind.Null => "",
            JsonValueKind.String => folderElement.GetString(),
            _ => null,
        };

        if (folderId == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Invalid 'folderId'. Must be string or null." });
            return true;
        }

        var document = FindDocument(project, documentId);
        if (document == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Document '{documentId}' not found." });
            return true;
        }

        if (!string.IsNullOrWhiteSpace(folderId))
        {
            DocFolder? folder = FindFolder(project, folderId);
            if (folder == null || folder.Scope != DocFolderScope.Documents)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"Folder '{folderId}' not found or not Documents scope." });
                return true;
            }
        }

        document.FolderId = string.IsNullOrWhiteSpace(folderId) ? null : folderId;
        SaveActiveProjectAndNotify(project);
        resultJson = JsonSerializer.Serialize(new
        {
            documentId = document.Id,
            folderId = document.FolderId ?? "",
            updated = true,
        });
        return true;
    }

    // ── Document block handlers ──

    private bool ToolBlockList(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        { isError = true; return true; }

        string documentId = GetArgString(args, "documentId");
        var doc = FindDocument(project, documentId);
        if (doc == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Document '{documentId}' not found." });
            return true;
        }

        bool includeText = GetArgBool(args, "includeText", true);
        var blocks = new List<object>(doc.Blocks.Count);
        for (int i = 0; i < doc.Blocks.Count; i++)
        {
            var b = doc.Blocks[i];
            blocks.Add(new
            {
                id = b.Id,
                index = i,
                order = b.Order,
                type = b.Type.ToString(),
                indentLevel = b.IndentLevel,
                @checked = b.Checked,
                language = string.IsNullOrEmpty(b.Language) ? null : b.Language,
                tableId = string.IsNullOrEmpty(b.TableId) ? null : b.TableId,
                tableVariantId = b.Type == DocBlockType.Table ? b.TableVariantId : (int?)null,
                viewId = string.IsNullOrEmpty(b.ViewId) ? null : b.ViewId,
                text = includeText && b.Type != DocBlockType.Table ? b.Text.PlainText : null,
                textPreview = b.Type == DocBlockType.Table ? null : TruncateText(b.Text.PlainText, 80),
            });
        }

        resultJson = JsonSerializer.Serialize(new { documentId = doc.Id, title = doc.Title, blocks });
        return true;
    }

    private bool ToolBlockAdd(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string documentId = GetArgString(args, "documentId");
        if (string.IsNullOrWhiteSpace(documentId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'documentId'." });
            return true;
        }

        var document = FindDocument(project, documentId);
        if (document == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Document '{documentId}' not found." });
            return true;
        }

        int requestedIndex = GetArgInt(args, "index", document.Blocks.Count);
        int insertIndex = Math.Clamp(requestedIndex, 0, document.Blocks.Count);

        DocBlockType blockType = DocBlockType.Paragraph;
        if (args.TryGetProperty("type", out var typeElement))
        {
            if (typeElement.ValueKind != JsonValueKind.String || !TryParseBlockType(typeElement.GetString(), out blockType))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = "Invalid 'type'." });
                return true;
            }
        }

        string text = GetArgString(args, "text");
        int indentLevel = GetArgInt(args, "indentLevel", 0);
        bool isChecked = GetArgBool(args, "checked", false);
        string language = GetArgString(args, "language");
        string tableId = GetArgString(args, "tableId");
        if (!TryReadOptionalVariantIdArg(args, "tableVariantId", DocTableVariant.BaseVariantId, out int tableVariantId, out string tableVariantError))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = tableVariantError });
            return true;
        }

        string viewId = GetArgString(args, "viewId");

        if (blockType == DocBlockType.Table)
        {
            if (string.IsNullOrWhiteSpace(tableId))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = "Table blocks require non-empty 'tableId'." });
                return true;
            }

            var table = FindTable(project, tableId);
            if (table == null)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"Table '{tableId}' not found." });
                return true;
            }

            if (tableVariantId < DocTableVariant.BaseVariantId || !HasTableVariant(table, tableVariantId))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"Variant '{tableVariantId}' not found." });
                return true;
            }

            if (!string.IsNullOrWhiteSpace(viewId) && !HasView(table, viewId))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"View '{viewId}' not found on table '{table.Id}'." });
                return true;
            }
        }
        else
        {
            tableId = "";
            tableVariantId = DocTableVariant.BaseVariantId;
            viewId = "";
        }

        string order = ComputeInsertedOrder(document.Blocks, insertIndex);
        var block = new DocBlock
        {
            Order = order,
            Type = blockType,
            IndentLevel = indentLevel,
            Checked = isChecked,
            Language = language,
            TableId = tableId,
            TableVariantId = tableVariantId,
            ViewId = viewId,
            Text = new RichText
            {
                PlainText = blockType == DocBlockType.Table ? "" : text,
            },
        };

        document.Blocks.Insert(insertIndex, block);
        NormalizeDocumentBlockOrders(document);
        SaveActiveProjectAndNotify(project);
        resultJson = JsonSerializer.Serialize(new
        {
            blockId = block.Id,
            index = insertIndex,
            order = block.Order,
            tableVariantId = block.TableVariantId,
        });
        return true;
    }

    private bool ToolBlockUpdate(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string documentId = GetArgString(args, "documentId");
        string blockId = GetArgString(args, "blockId");
        if (string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(blockId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'documentId' or 'blockId'." });
            return true;
        }

        var document = FindDocument(project, documentId);
        if (document == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Document '{documentId}' not found." });
            return true;
        }

        var block = FindBlock(document, blockId);
        if (block == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Block '{blockId}' not found." });
            return true;
        }

        bool updated = false;

        if (args.TryGetProperty("type", out var typeElement))
        {
            if (typeElement.ValueKind != JsonValueKind.String || !TryParseBlockType(typeElement.GetString(), out var parsedType))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = "Invalid 'type'." });
                return true;
            }

            if (block.Type != parsedType)
            {
                block.Type = parsedType;
                updated = true;
            }
        }

        if (args.TryGetProperty("text", out var textElement))
        {
            if (textElement.ValueKind != JsonValueKind.String)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = "Invalid 'text'. Expected string." });
                return true;
            }

            string newText = textElement.GetString() ?? "";
            if (!string.Equals(block.Text.PlainText, newText, StringComparison.Ordinal))
            {
                block.Text.PlainText = newText;
                block.Text.Spans.Clear();
                updated = true;
            }
        }

        if (args.TryGetProperty("indentLevel", out var indentElement))
        {
            if (indentElement.ValueKind != JsonValueKind.Number)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = "Invalid 'indentLevel'. Expected number." });
                return true;
            }

            int indentLevel = indentElement.GetInt32();
            if (block.IndentLevel != indentLevel)
            {
                block.IndentLevel = indentLevel;
                updated = true;
            }
        }

        if (args.TryGetProperty("checked", out var checkedElement))
        {
            if (checkedElement.ValueKind != JsonValueKind.True && checkedElement.ValueKind != JsonValueKind.False)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = "Invalid 'checked'. Expected boolean." });
                return true;
            }

            bool checkedValue = checkedElement.GetBoolean();
            if (block.Checked != checkedValue)
            {
                block.Checked = checkedValue;
                updated = true;
            }
        }

        if (args.TryGetProperty("language", out var languageElement))
        {
            if (languageElement.ValueKind != JsonValueKind.String)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = "Invalid 'language'. Expected string." });
                return true;
            }

            string nextLanguage = languageElement.GetString() ?? "";
            if (!string.Equals(block.Language, nextLanguage, StringComparison.Ordinal))
            {
                block.Language = nextLanguage;
                updated = true;
            }
        }

        if (args.TryGetProperty("tableId", out var tableElement))
        {
            if (tableElement.ValueKind != JsonValueKind.String && tableElement.ValueKind != JsonValueKind.Null)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = "Invalid 'tableId'. Must be string or null." });
                return true;
            }

            string nextTableId = tableElement.ValueKind == JsonValueKind.Null ? "" : (tableElement.GetString() ?? "");
            if (!string.Equals(block.TableId, nextTableId, StringComparison.Ordinal))
            {
                block.TableId = nextTableId;
                updated = true;
            }
        }

        if (args.TryGetProperty("tableVariantId", out JsonElement tableVariantElement))
        {
            if (tableVariantElement.ValueKind != JsonValueKind.Number && tableVariantElement.ValueKind != JsonValueKind.Null)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = "Invalid 'tableVariantId'. Must be number or null." });
                return true;
            }

            int nextTableVariantId = DocTableVariant.BaseVariantId;
            if (tableVariantElement.ValueKind == JsonValueKind.Number &&
                !tableVariantElement.TryGetInt32(out nextTableVariantId))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = "Invalid 'tableVariantId'. Must be an integer." });
                return true;
            }

            if (block.TableVariantId != nextTableVariantId)
            {
                block.TableVariantId = nextTableVariantId;
                updated = true;
            }
        }

        if (args.TryGetProperty("viewId", out var viewElement))
        {
            if (viewElement.ValueKind != JsonValueKind.String && viewElement.ValueKind != JsonValueKind.Null)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = "Invalid 'viewId'. Must be string or null." });
                return true;
            }

            string nextViewId = viewElement.ValueKind == JsonValueKind.Null ? "" : (viewElement.GetString() ?? "");
            if (!string.Equals(block.ViewId, nextViewId, StringComparison.Ordinal))
            {
                block.ViewId = nextViewId;
                updated = true;
            }
        }

        if (block.Type == DocBlockType.Table)
        {
            if (string.IsNullOrWhiteSpace(block.TableId))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = "Table block requires non-empty tableId." });
                return true;
            }

            var table = FindTable(project, block.TableId);
            if (table == null)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"Table '{block.TableId}' not found." });
                return true;
            }

            if (!string.IsNullOrWhiteSpace(block.ViewId) && !HasView(table, block.ViewId))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"View '{block.ViewId}' not found on table '{table.Id}'." });
                return true;
            }

            if (block.TableVariantId < DocTableVariant.BaseVariantId ||
                !HasTableVariant(table, block.TableVariantId))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"Variant '{block.TableVariantId}' not found." });
                return true;
            }

            if (!string.IsNullOrEmpty(block.Text.PlainText))
            {
                block.Text.PlainText = "";
                block.Text.Spans.Clear();
                updated = true;
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(block.TableId))
            {
                block.TableId = "";
                updated = true;
            }
            if (!string.IsNullOrEmpty(block.ViewId))
            {
                block.ViewId = "";
                updated = true;
            }

            if (block.TableVariantId != DocTableVariant.BaseVariantId)
            {
                block.TableVariantId = DocTableVariant.BaseVariantId;
                updated = true;
            }
        }

        if (updated)
        {
            SaveActiveProjectAndNotify(project);
        }

        resultJson = JsonSerializer.Serialize(new { blockId = block.Id, tableVariantId = block.TableVariantId, updated });
        return true;
    }

    private bool ToolBlockDelete(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string documentId = GetArgString(args, "documentId");
        string blockId = GetArgString(args, "blockId");
        if (string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(blockId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'documentId' or 'blockId'." });
            return true;
        }

        var document = FindDocument(project, documentId);
        if (document == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Document '{documentId}' not found." });
            return true;
        }

        int blockIndex = FindBlockIndex(document, blockId);
        if (blockIndex < 0)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Block '{blockId}' not found." });
            return true;
        }

        document.Blocks.RemoveAt(blockIndex);
        NormalizeDocumentBlockOrders(document);
        SaveActiveProjectAndNotify(project);
        resultJson = JsonSerializer.Serialize(new { deleted = true });
        return true;
    }

    private bool ToolBlockMove(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        {
            isError = true;
            return true;
        }

        string documentId = GetArgString(args, "documentId");
        string blockId = GetArgString(args, "blockId");
        if (string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(blockId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'documentId' or 'blockId'." });
            return true;
        }

        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("index", out var indexElement) || indexElement.ValueKind != JsonValueKind.Number)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = "Missing 'index'." });
            return true;
        }

        int targetIndex = indexElement.GetInt32();
        var document = FindDocument(project, documentId);
        if (document == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Document '{documentId}' not found." });
            return true;
        }

        int currentIndex = FindBlockIndex(document, blockId);
        if (currentIndex < 0)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Block '{blockId}' not found." });
            return true;
        }

        var block = document.Blocks[currentIndex];
        document.Blocks.RemoveAt(currentIndex);

        int clampedIndex = Math.Clamp(targetIndex, 0, document.Blocks.Count);
        document.Blocks.Insert(clampedIndex, block);
        NormalizeDocumentBlockOrders(document);

        SaveActiveProjectAndNotify(project);
        resultJson = JsonSerializer.Serialize(new
        {
            blockId = block.Id,
            index = clampedIndex,
            order = block.Order,
            updated = true,
        });
        return true;
    }

    private bool ToolBlockViewSet(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        { isError = true; return true; }

        string documentId = GetArgString(args, "documentId");
        string blockId = GetArgString(args, "blockId");
        string viewId = GetArgString(args, "viewId");

        var doc = FindDocument(project, documentId);
        if (doc == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Document '{documentId}' not found." });
            return true;
        }

        var block = FindBlock(doc, blockId);
        if (block == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Block '{blockId}' not found." });
            return true;
        }

        if (block.Type != DocBlockType.Table)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Block '{blockId}' is not a Table block." });
            return true;
        }

        if (!string.IsNullOrWhiteSpace(viewId))
        {
            if (string.IsNullOrWhiteSpace(block.TableId))
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"Block '{blockId}' is not an embedded Table block." });
                return true;
            }

            var table = FindTable(project, block.TableId);
            if (table == null)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"Table '{block.TableId}' not found for block." });
                return true;
            }

            bool foundView = false;
            for (int viewIndex = 0; viewIndex < table.Views.Count; viewIndex++)
            {
                if (string.Equals(table.Views[viewIndex].Id, viewId, StringComparison.Ordinal))
                {
                    foundView = true;
                    break;
                }
            }

            if (!foundView)
            {
                isError = true;
                resultJson = JsonSerializer.Serialize(new { error = $"View '{viewId}' not found on table '{table.Id}'." });
                return true;
            }
        }

        block.ViewId = string.IsNullOrWhiteSpace(viewId) ? "" : viewId;
        SaveActiveProjectAndNotify(project);
        resultJson = JsonSerializer.Serialize(new { blockId = block.Id, viewId = block.ViewId, updated = true });
        return true;
    }

    private bool ToolBlockViewCreate(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        { isError = true; return true; }

        string documentId = GetArgString(args, "documentId");
        string blockId = GetArgString(args, "blockId");

        var doc = FindDocument(project, documentId);
        if (doc == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Document '{documentId}' not found." });
            return true;
        }

        var block = FindBlock(doc, blockId);
        if (block == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Block '{blockId}' not found." });
            return true;
        }

        if (block.Type != DocBlockType.Table || string.IsNullOrEmpty(block.TableId))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Block '{blockId}' is not an embedded Table block." });
            return true;
        }

        var table = FindTable(project, block.TableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table '{block.TableId}' not found for block." });
            return true;
        }

        string name = GetArgString(args, "name");
        string typeStr = GetArgString(args, "type");
        var viewType = typeStr.ToLowerInvariant() switch
        {
            "board" => DocViewType.Board,
            "calendar" => DocViewType.Calendar,
            "chart" => DocViewType.Chart,
            "custom" => DocViewType.Custom,
            _ => DocViewType.Grid,
        };

        var view = new DocView
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"{viewType} view" : name,
            Type = viewType,
            CustomRendererId = GetArgStringOrNull(args, "customRendererId"),
            GroupByColumnId = GetArgStringOrNull(args, "groupByColumnId"),
            CalendarDateColumnId = GetArgStringOrNull(args, "calendarDateColumnId"),
            ChartCategoryColumnId = GetArgStringOrNull(args, "chartCategoryColumnId"),
            ChartValueColumnId = GetArgStringOrNull(args, "chartValueColumnId"),
        };
        if (view.Type != DocViewType.Custom)
        {
            view.CustomRendererId = null;
        }

        string bvChartKind = GetArgString(args, "chartKind");
        if (!string.IsNullOrWhiteSpace(bvChartKind) && Enum.TryParse<DocChartKind>(bvChartKind, true, out var bvCk))
            view.ChartKind = bvCk;

        // VisibleColumnIds
        if (args.TryGetProperty("visibleColumnIds", out var visCols) && visCols.ValueKind == JsonValueKind.Array)
        {
            view.VisibleColumnIds = new List<string>();
            foreach (var el in visCols.EnumerateArray())
            {
                string? s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s)) view.VisibleColumnIds.Add(s);
            }
        }

        ParseFiltersFromArgs(args, view);
        ParseSortsFromArgs(args, view);

        table.Views.Add(view);
        block.ViewId = view.Id;

        SaveActiveProjectAndNotify(project);
        resultJson = JsonSerializer.Serialize(new
        {
            viewId = view.Id,
            blockId = block.Id,
            name = view.Name,
            type = view.Type.ToString(),
            customRendererId = view.CustomRendererId
        });
        return true;
    }

    // ── Derived table handlers ──

    private bool ToolDerivedGet(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        { isError = true; return true; }

        string tableId = GetArgString(args, "tableId");
        var table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table '{tableId}' not found." });
            return true;
        }

        if (!table.IsDerived || table.DerivedConfig == null)
        {
            resultJson = JsonSerializer.Serialize(new { tableId = table.Id, isDerived = false, config = (object?)null });
            return true;
        }

        var cfg = table.DerivedConfig;
        var steps = new List<object>(cfg.Steps.Count);
        for (int i = 0; i < cfg.Steps.Count; i++)
        {
            var s = cfg.Steps[i];
            var keyMappings = new List<object>(s.KeyMappings.Count);
            for (int k = 0; k < s.KeyMappings.Count; k++)
            {
                keyMappings.Add(new { baseColumnId = s.KeyMappings[k].BaseColumnId, sourceColumnId = s.KeyMappings[k].SourceColumnId });
            }
            steps.Add(new
            {
                id = s.Id,
                kind = s.Kind.ToString(),
                sourceTableId = s.SourceTableId,
                joinKind = s.JoinKind.ToString(),
                keyMappings,
            });
        }

        var projections = new List<object>(cfg.Projections.Count);
        for (int i = 0; i < cfg.Projections.Count; i++)
        {
            var p = cfg.Projections[i];
            projections.Add(new
            {
                sourceTableId = p.SourceTableId,
                sourceColumnId = p.SourceColumnId,
                outputColumnId = p.OutputColumnId,
                renameAlias = p.RenameAlias,
            });
        }

        resultJson = JsonSerializer.Serialize(new
        {
            tableId = table.Id,
            isDerived = true,
            config = new
            {
                baseTableId = cfg.BaseTableId,
                filterExpression = cfg.FilterExpression,
                steps,
                projections,
            },
        });
        return true;
    }

    private bool ToolDerivedSet(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        { isError = true; return true; }

        string tableId = GetArgString(args, "tableId");
        var table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table '{tableId}' not found." });
            return true;
        }

        if (DocSystemTableRules.IsSchemaLocked(table))
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"System table '{table.Name}' does not allow derived config edits." });
            return true;
        }

        // Check if config is null or absent → clear derived status
        if (!args.TryGetProperty("config", out var cfgEl) || cfgEl.ValueKind == JsonValueKind.Null)
        {
            table.DerivedConfig = null;
            SaveActiveProjectAndNotify(project);
            resultJson = JsonSerializer.Serialize(new { tableId = table.Id, isDerived = table.IsDerived, updated = true });
            return true;
        }

        // Build derived config
        var config = new DocDerivedConfig();

        if (cfgEl.TryGetProperty("baseTableId", out var btProp) && btProp.ValueKind == JsonValueKind.String)
            config.BaseTableId = btProp.GetString();

        if (cfgEl.TryGetProperty("filterExpression", out var filterProp) && filterProp.ValueKind == JsonValueKind.String)
        {
            config.FilterExpression = filterProp.GetString() ?? "";
        }

        // Steps
        if (cfgEl.TryGetProperty("steps", out var stepsEl) && stepsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var sEl in stepsEl.EnumerateArray())
            {
                if (sEl.ValueKind != JsonValueKind.Object) continue;

                var step = new DerivedStep
                {
                    SourceTableId = GetStringOrEmpty(sEl, "sourceTableId"),
                };

                string kindStr = GetStringOrEmpty(sEl, "kind");
                step.Kind = kindStr.ToLowerInvariant() switch
                {
                    "join" => DerivedStepKind.Join,
                    _ => DerivedStepKind.Append,
                };

                string joinKindStr = GetStringOrEmpty(sEl, "joinKind");
                step.JoinKind = joinKindStr.ToLowerInvariant() switch
                {
                    "inner" => DerivedJoinKind.Inner,
                    "fullouter" => DerivedJoinKind.FullOuter,
                    _ => DerivedJoinKind.Left,
                };

                if (sEl.TryGetProperty("keyMappings", out var kmEl) && kmEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var kmItem in kmEl.EnumerateArray())
                    {
                        if (kmItem.ValueKind != JsonValueKind.Object) continue;
                        step.KeyMappings.Add(new DerivedKeyMapping
                        {
                            BaseColumnId = GetStringOrEmpty(kmItem, "baseColumnId"),
                            SourceColumnId = GetStringOrEmpty(kmItem, "sourceColumnId"),
                        });
                    }
                }

                config.Steps.Add(step);
            }
        }

        // Projections
        if (cfgEl.TryGetProperty("projections", out var projEl) && projEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var pEl in projEl.EnumerateArray())
            {
                if (pEl.ValueKind != JsonValueKind.Object) continue;
                config.Projections.Add(new DerivedProjection
                {
                    SourceTableId = GetStringOrEmpty(pEl, "sourceTableId"),
                    SourceColumnId = GetStringOrEmpty(pEl, "sourceColumnId"),
                    OutputColumnId = GetStringOrEmpty(pEl, "outputColumnId"),
                    RenameAlias = GetStringOrEmpty(pEl, "renameAlias"),
                });
            }
        }

        table.DerivedConfig = config;
        SaveActiveProjectAndNotify(project);
        resultJson = JsonSerializer.Serialize(new { tableId = table.Id, isDerived = table.IsDerived, updated = true });
        return true;
    }

    // ── Formula handlers ──

    private bool ToolFormulaValidate(JsonElement args, out string resultJson, out bool isError)
    {
        resultJson = "";
        isError = false;

        if (!TryLoadActiveProject(out var project, out resultJson))
        { isError = true; return true; }

        string tableId = GetArgString(args, "tableId");
        string expression = GetArgString(args, "expression");

        var table = FindTable(project, tableId);
        if (table == null)
        {
            isError = true;
            resultJson = JsonSerializer.Serialize(new { error = $"Table '{tableId}' not found." });
            return true;
        }

        if (string.IsNullOrWhiteSpace(expression))
        {
            resultJson = JsonSerializer.Serialize(new { valid = false, error = "Expression is empty." });
            return true;
        }

        // Try compile-only validation first
        if (!DocFormulaEngine.ValidateExpression(expression))
        {
            resultJson = JsonSerializer.Serialize(new { valid = false, error = (string?)"Formula compilation failed. Check syntax." });
            return true;
        }

        // If rows exist, also try evaluation against the first row for deeper validation
        if (table.Rows.Count > 0)
        {
            var engine = new DocFormulaEngine();
            var testCol = new DocColumn { Kind = DocColumnKind.Formula, FormulaExpression = expression };
            bool ok = engine.TryEvaluateExpression(project, table, table.Rows[0], testCol, expression, out _);
            resultJson = JsonSerializer.Serialize(ok
                ? new { valid = true, error = (string?)null }
                : new { valid = false, error = (string?)"Formula compiles but evaluation failed against first row." });
        }
        else
        {
            resultJson = JsonSerializer.Serialize(new { valid = true, error = (string?)null });
        }

        return true;
    }

    private static bool TryReadRequiredVariantIdArg(
        JsonElement args,
        string propertyName,
        out int variantId,
        out string errorMessage)
    {
        variantId = DocTableVariant.BaseVariantId;
        errorMessage = "";

        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(propertyName, out JsonElement variantElement))
        {
            errorMessage = $"Missing '{propertyName}'.";
            return false;
        }

        if (variantElement.ValueKind != JsonValueKind.Number || !variantElement.TryGetInt32(out variantId))
        {
            errorMessage = $"Invalid '{propertyName}'. Must be an integer.";
            return false;
        }

        if (variantId < DocTableVariant.BaseVariantId)
        {
            errorMessage = $"Invalid '{propertyName}'. Variant ids must be >= 0.";
            return false;
        }

        return true;
    }

    private static bool TryReadOptionalVariantIdArg(
        JsonElement args,
        string propertyName,
        int fallbackVariantId,
        out int variantId,
        out string errorMessage)
    {
        variantId = fallbackVariantId;
        errorMessage = "";

        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(propertyName, out JsonElement variantElement))
        {
            return true;
        }

        if (variantElement.ValueKind == JsonValueKind.Null)
        {
            variantId = DocTableVariant.BaseVariantId;
            return true;
        }

        if (variantElement.ValueKind != JsonValueKind.Number || !variantElement.TryGetInt32(out variantId))
        {
            errorMessage = $"Invalid '{propertyName}'. Must be an integer.";
            return false;
        }

        if (variantId < DocTableVariant.BaseVariantId)
        {
            errorMessage = $"Invalid '{propertyName}'. Variant ids must be >= 0.";
            return false;
        }

        return true;
    }

    private static bool HasTableVariant(DocTable table, int variantId)
    {
        if (variantId == DocTableVariant.BaseVariantId)
        {
            return true;
        }

        for (int variantIndex = 0; variantIndex < table.Variants.Count; variantIndex++)
        {
            if (table.Variants[variantIndex].Id == variantId)
            {
                return true;
            }
        }

        return false;
    }

    private static int FindTableVariantIndex(DocTable table, int variantId)
    {
        for (int variantIndex = 0; variantIndex < table.Variants.Count; variantIndex++)
        {
            if (table.Variants[variantIndex].Id == variantId)
            {
                return variantIndex;
            }
        }

        return -1;
    }

    private static DocTableVariantDelta GetOrCreateVariantDelta(DocTable table, int variantId)
    {
        for (int deltaIndex = 0; deltaIndex < table.VariantDeltas.Count; deltaIndex++)
        {
            DocTableVariantDelta currentDelta = table.VariantDeltas[deltaIndex];
            if (currentDelta.VariantId == variantId)
            {
                return currentDelta;
            }
        }

        var createdDelta = new DocTableVariantDelta
        {
            VariantId = variantId,
        };
        table.VariantDeltas.Add(createdDelta);
        return createdDelta;
    }

    private static bool TryGetVariantDelta(DocTable table, int variantId, out DocTableVariantDelta? variantDelta)
    {
        for (int deltaIndex = 0; deltaIndex < table.VariantDeltas.Count; deltaIndex++)
        {
            DocTableVariantDelta currentDelta = table.VariantDeltas[deltaIndex];
            if (currentDelta.VariantId == variantId)
            {
                variantDelta = currentDelta;
                return true;
            }
        }

        variantDelta = null;
        return false;
    }

    private static DocRow? FindVariantRow(
        DocTable table,
        DocTableVariantDelta variantDelta,
        string rowId,
        out bool rowIsAdded,
        out bool rowIsDeletedBase)
    {
        rowIsAdded = false;
        rowIsDeletedBase = false;

        int addedRowIndex = FindAddedRowIndex(variantDelta, rowId);
        if (addedRowIndex >= 0)
        {
            rowIsAdded = true;
            return variantDelta.AddedRows[addedRowIndex];
        }

        for (int deletedRowIndex = 0; deletedRowIndex < variantDelta.DeletedBaseRowIds.Count; deletedRowIndex++)
        {
            if (string.Equals(variantDelta.DeletedBaseRowIds[deletedRowIndex], rowId, StringComparison.Ordinal))
            {
                rowIsDeletedBase = true;
                return null;
            }
        }

        return FindRow(table, rowId);
    }

    private static int FindAddedRowIndex(DocTableVariantDelta variantDelta, string rowId)
    {
        for (int rowIndex = 0; rowIndex < variantDelta.AddedRows.Count; rowIndex++)
        {
            if (string.Equals(variantDelta.AddedRows[rowIndex].Id, rowId, StringComparison.Ordinal))
            {
                return rowIndex;
            }
        }

        return -1;
    }

    private static void ApplyVariantCellOverrides(
        DocTable table,
        DocTableVariantDelta variantDelta,
        string rowId,
        JsonElement cells)
    {
        foreach (JsonProperty cellEntry in cells.EnumerateObject())
        {
            string columnId = cellEntry.Name;
            DocColumn? column = FindColumn(table, columnId);
            if (column == null)
            {
                continue;
            }

            if (!TryConvertToolCellValue(column, cellEntry.Value, out DocCellValue convertedCellValue))
            {
                continue;
            }

            int existingOverrideIndex = FindCellOverrideIndex(variantDelta, rowId, columnId);
            if (existingOverrideIndex >= 0)
            {
                variantDelta.CellOverrides[existingOverrideIndex].Value = convertedCellValue.Clone();
            }
            else
            {
                variantDelta.CellOverrides.Add(new DocTableCellOverride
                {
                    RowId = rowId,
                    ColumnId = columnId,
                    Value = convertedCellValue.Clone(),
                });
            }
        }
    }

    private static int FindCellOverrideIndex(
        DocTableVariantDelta variantDelta,
        string rowId,
        string columnId)
    {
        for (int overrideIndex = 0; overrideIndex < variantDelta.CellOverrides.Count; overrideIndex++)
        {
            DocTableCellOverride cellOverride = variantDelta.CellOverrides[overrideIndex];
            if (string.Equals(cellOverride.RowId, rowId, StringComparison.Ordinal) &&
                string.Equals(cellOverride.ColumnId, columnId, StringComparison.Ordinal))
            {
                return overrideIndex;
            }
        }

        return -1;
    }

    private static void RemoveVariantCellOverridesForRow(DocTableVariantDelta variantDelta, string rowId)
    {
        for (int overrideIndex = variantDelta.CellOverrides.Count - 1; overrideIndex >= 0; overrideIndex--)
        {
            if (string.Equals(variantDelta.CellOverrides[overrideIndex].RowId, rowId, StringComparison.Ordinal))
            {
                variantDelta.CellOverrides.RemoveAt(overrideIndex);
            }
        }
    }

    private bool TryReadNanobananaConfiguration(
        out string apiBaseUrl,
        out string apiKey,
        out string errorMessage)
    {
        apiBaseUrl = "";
        apiKey = "";
        errorMessage = "";

        DocUserPreferences preferences;
        try
        {
            preferences = _userPreferencesReader();
        }
        catch (Exception ex)
        {
            errorMessage = "Failed to read user preferences: " + ex.Message;
            return false;
        }

        if (!preferences.TryGetPluginSetting(NanobananaApiKeyPreferenceKey, out string configuredApiKey) ||
            string.IsNullOrWhiteSpace(configuredApiKey))
        {
            errorMessage = "Missing global preference '" + NanobananaApiKeyPreferenceKey + "' in " +
                           DocUserPreferencesFile.GetPath() + ".";
            return false;
        }

        string configuredBaseUrl = "";
        if (preferences.TryGetPluginSetting(NanobananaApiBaseUrlPreferenceKey, out string configuredBaseUrlValue))
        {
            configuredBaseUrl = configuredBaseUrlValue;
        }

        string normalizedConfiguredBaseUrl = configuredBaseUrl.Trim();
        apiBaseUrl = string.IsNullOrWhiteSpace(normalizedConfiguredBaseUrl)
            ? NanobananaDefaultApiBaseUrl
            : normalizedConfiguredBaseUrl;
        apiKey = configuredApiKey.Trim();
        return true;
    }

    private bool TryReadElevenLabsConfiguration(
        out string apiBaseUrl,
        out string apiKey,
        out string errorMessage)
    {
        apiBaseUrl = "";
        apiKey = "";
        errorMessage = "";

        DocUserPreferences preferences;
        try
        {
            preferences = _userPreferencesReader();
        }
        catch (Exception ex)
        {
            errorMessage = "Failed to read user preferences: " + ex.Message;
            return false;
        }

        if (!preferences.TryGetPluginSetting(ElevenLabsApiKeyPreferenceKey, out string configuredApiKey) ||
            string.IsNullOrWhiteSpace(configuredApiKey))
        {
            errorMessage = "Missing global preference '" + ElevenLabsApiKeyPreferenceKey + "' in " +
                           DocUserPreferencesFile.GetPath() + ".";
            return false;
        }

        string configuredBaseUrl = "";
        if (preferences.TryGetPluginSetting(ElevenLabsApiBaseUrlPreferenceKey, out string configuredBaseUrlValue))
        {
            configuredBaseUrl = configuredBaseUrlValue;
        }

        string normalizedConfiguredBaseUrl = configuredBaseUrl.Trim();
        apiBaseUrl = string.IsNullOrWhiteSpace(normalizedConfiguredBaseUrl)
            ? ElevenLabsDefaultApiBaseUrl
            : normalizedConfiguredBaseUrl;
        apiKey = configuredApiKey.Trim();
        return true;
    }

    private static bool TryBuildElevenLabsTextToSpeechRequest(
        JsonElement requestPayload,
        out string voiceId,
        out string outputFormat,
        out bool? enableLogging,
        out string requestJson,
        out string errorMessage)
    {
        voiceId = "";
        outputFormat = ElevenLabsDefaultOutputFormat;
        enableLogging = null;
        requestJson = "{}";
        errorMessage = "";

        if (requestPayload.ValueKind != JsonValueKind.Object)
        {
            errorMessage = "request must be a JSON object.";
            return false;
        }

        if (!TryReadElevenLabsVoiceConfiguration(
                requestPayload,
                out voiceId,
                out outputFormat,
                out enableLogging,
                out errorMessage))
        {
            return false;
        }

        if (!TryBuildRequestJsonWithoutExcludedProperties(
                requestPayload,
                ["voiceId", "voice_id", "outputFormat", "output_format", "enableLogging", "enable_logging"],
                out requestJson,
                out errorMessage))
        {
            return false;
        }

        using JsonDocument requestDocument = JsonDocument.Parse(requestJson);
        if (requestDocument.RootElement.ValueKind != JsonValueKind.Object)
        {
            errorMessage = "ElevenLabs request body must be a JSON object.";
            return false;
        }

        string text = GetStringOrEmpty(requestDocument.RootElement, "text").Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            errorMessage = "ElevenLabs text-to-speech request requires a non-empty text field.";
            return false;
        }

        return true;
    }

    private static bool TryBuildElevenLabsSpeechToSpeechRequest(
        JsonElement requestPayload,
        out string voiceId,
        out string outputFormat,
        out bool? enableLogging,
        out string requestJson,
        out byte[] inputAudioBytes,
        out string inputAudioFileName,
        out string inputAudioMimeType,
        out string errorMessage)
    {
        voiceId = "";
        outputFormat = ElevenLabsDefaultOutputFormat;
        enableLogging = null;
        requestJson = "{}";
        inputAudioBytes = Array.Empty<byte>();
        inputAudioFileName = "";
        inputAudioMimeType = "";
        errorMessage = "";

        if (requestPayload.ValueKind != JsonValueKind.Object)
        {
            errorMessage = "request must be a JSON object.";
            return false;
        }

        if (!TryReadElevenLabsVoiceConfiguration(
                requestPayload,
                out voiceId,
                out outputFormat,
                out enableLogging,
                out errorMessage))
        {
            return false;
        }

        if (!TryReadElevenLabsSpeechInputAudio(
                requestPayload,
                out inputAudioBytes,
                out inputAudioFileName,
                out inputAudioMimeType,
                out errorMessage))
        {
            return false;
        }

        if (!TryBuildRequestJsonWithoutExcludedProperties(
                requestPayload,
                [
                    "voiceId",
                    "voice_id",
                    "outputFormat",
                    "output_format",
                    "enableLogging",
                    "enable_logging",
                    "audioBase64",
                    "audio_base64",
                    "audioDataUrl",
                    "audio_data_url",
                    "inputFileName",
                    "input_file_name",
                    "inputMimeType",
                    "input_mime_type"
                ],
                out requestJson,
                out errorMessage))
        {
            return false;
        }

        return true;
    }

    private static bool TryReadElevenLabsVoiceConfiguration(
        JsonElement requestPayload,
        out string voiceId,
        out string outputFormat,
        out bool? enableLogging,
        out string errorMessage)
    {
        voiceId = "";
        outputFormat = ElevenLabsDefaultOutputFormat;
        enableLogging = null;
        errorMessage = "";

        string configuredVoiceId = GetStringOrEmpty(requestPayload, "voiceId");
        if (string.IsNullOrWhiteSpace(configuredVoiceId))
        {
            configuredVoiceId = GetStringOrEmpty(requestPayload, "voice_id");
        }

        configuredVoiceId = configuredVoiceId.Trim();
        if (string.IsNullOrWhiteSpace(configuredVoiceId))
        {
            errorMessage = "request.voiceId is required for ElevenLabs calls.";
            return false;
        }

        string configuredOutputFormat = GetStringOrEmpty(requestPayload, "outputFormat");
        if (string.IsNullOrWhiteSpace(configuredOutputFormat))
        {
            configuredOutputFormat = GetStringOrEmpty(requestPayload, "output_format");
        }

        configuredOutputFormat = configuredOutputFormat.Trim();
        if (!string.IsNullOrWhiteSpace(configuredOutputFormat))
        {
            outputFormat = configuredOutputFormat;
        }

        enableLogging = TryReadElevenLabsEnableLogging(requestPayload);
        voiceId = configuredVoiceId;
        return true;
    }

    private static bool? TryReadElevenLabsEnableLogging(JsonElement requestPayload)
    {
        if (requestPayload.TryGetProperty("enableLogging", out JsonElement camelCaseElement))
        {
            if (camelCaseElement.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (camelCaseElement.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            if (camelCaseElement.ValueKind == JsonValueKind.String)
            {
                if (bool.TryParse(camelCaseElement.GetString(), out bool parsedValue))
                {
                    return parsedValue;
                }
            }
        }

        if (requestPayload.TryGetProperty("enable_logging", out JsonElement snakeCaseElement))
        {
            if (snakeCaseElement.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (snakeCaseElement.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            if (snakeCaseElement.ValueKind == JsonValueKind.String)
            {
                if (bool.TryParse(snakeCaseElement.GetString(), out bool parsedValue))
                {
                    return parsedValue;
                }
            }
        }

        return null;
    }

    private static bool TryReadElevenLabsSpeechInputAudio(
        JsonElement requestPayload,
        out byte[] audioBytes,
        out string inputAudioFileName,
        out string inputAudioMimeType,
        out string errorMessage)
    {
        audioBytes = Array.Empty<byte>();
        inputAudioFileName = "";
        inputAudioMimeType = "";
        errorMessage = "";

        string configuredFileName = GetStringOrEmpty(requestPayload, "inputFileName");
        if (string.IsNullOrWhiteSpace(configuredFileName))
        {
            configuredFileName = GetStringOrEmpty(requestPayload, "input_file_name");
        }

        string configuredMimeType = GetStringOrEmpty(requestPayload, "inputMimeType");
        if (string.IsNullOrWhiteSpace(configuredMimeType))
        {
            configuredMimeType = GetStringOrEmpty(requestPayload, "input_mime_type");
        }

        string audioBase64 = GetStringOrEmpty(requestPayload, "audioBase64");
        if (string.IsNullOrWhiteSpace(audioBase64))
        {
            audioBase64 = GetStringOrEmpty(requestPayload, "audio_base64");
        }

        audioBase64 = audioBase64.Trim();
        if (!string.IsNullOrWhiteSpace(audioBase64))
        {
            if (!TryDecodeBase64(audioBase64, out audioBytes))
            {
                errorMessage = "request.audioBase64 is not valid base64.";
                return false;
            }

            inputAudioMimeType = string.IsNullOrWhiteSpace(configuredMimeType) ? "audio/mpeg" : configuredMimeType.Trim();
            inputAudioFileName = ResolveElevenLabsInputAudioFileName(configuredFileName, inputAudioMimeType);
            return true;
        }

        string audioDataUrl = GetStringOrEmpty(requestPayload, "audioDataUrl");
        if (string.IsNullOrWhiteSpace(audioDataUrl))
        {
            audioDataUrl = GetStringOrEmpty(requestPayload, "audio_data_url");
        }

        audioDataUrl = audioDataUrl.Trim();
        if (string.IsNullOrWhiteSpace(audioDataUrl))
        {
            errorMessage = "ElevenLabs edit request requires audioBase64 or audioDataUrl.";
            return false;
        }

        if (!TryParseDataUrl(audioDataUrl, out string parsedMimeType, out string parsedBase64, out string parseError))
        {
            errorMessage = parseError;
            return false;
        }

        if (!TryDecodeBase64(parsedBase64, out audioBytes))
        {
            errorMessage = "request.audioDataUrl does not contain valid base64 data.";
            return false;
        }

        inputAudioMimeType = string.IsNullOrWhiteSpace(configuredMimeType) ? parsedMimeType : configuredMimeType.Trim();
        inputAudioFileName = ResolveElevenLabsInputAudioFileName(configuredFileName, inputAudioMimeType);
        return true;
    }

    private static string ResolveElevenLabsInputAudioFileName(string configuredFileName, string mimeType)
    {
        string candidateFileName = Path.GetFileName((configuredFileName ?? "").Trim());
        if (!string.IsNullOrWhiteSpace(candidateFileName))
        {
            return candidateFileName;
        }

        if (mimeType.Contains("wav", StringComparison.OrdinalIgnoreCase))
        {
            return "input_audio.wav";
        }

        if (mimeType.Contains("ogg", StringComparison.OrdinalIgnoreCase))
        {
            return "input_audio.ogg";
        }

        if (mimeType.Contains("webm", StringComparison.OrdinalIgnoreCase))
        {
            return "input_audio.webm";
        }

        if (mimeType.Contains("flac", StringComparison.OrdinalIgnoreCase))
        {
            return "input_audio.flac";
        }

        return "input_audio.mp3";
    }

    private static bool TryBuildRequestJsonWithoutExcludedProperties(
        JsonElement requestPayload,
        string[] excludedPropertyNames,
        out string requestJson,
        out string errorMessage)
    {
        requestJson = "{}";
        errorMessage = "";

        if (requestPayload.ValueKind != JsonValueKind.Object)
        {
            errorMessage = "request must be a JSON object.";
            return false;
        }

        var requestWriterBuffer = new ArrayBufferWriter<byte>(2048);
        using (var writer = new Utf8JsonWriter(requestWriterBuffer))
        {
            writer.WriteStartObject();

            foreach (JsonProperty property in requestPayload.EnumerateObject())
            {
                if (IsExcludedPropertyName(property.Name, excludedPropertyNames))
                {
                    continue;
                }

                writer.WritePropertyName(property.Name);
                property.Value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        requestJson = Encoding.UTF8.GetString(requestWriterBuffer.WrittenSpan);
        return true;
    }

    private static bool IsExcludedPropertyName(string propertyName, string[] excludedPropertyNames)
    {
        for (int excludedIndex = 0; excludedIndex < excludedPropertyNames.Length; excludedIndex++)
        {
            if (string.Equals(propertyName, excludedPropertyNames[excludedIndex], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseDataUrl(
        string dataUrl,
        out string mimeType,
        out string base64Data,
        out string errorMessage)
    {
        mimeType = "";
        base64Data = "";
        errorMessage = "";

        if (!dataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "audioDataUrl must start with 'data:'.";
            return false;
        }

        int commaIndex = dataUrl.IndexOf(',');
        if (commaIndex <= 5 || commaIndex >= dataUrl.Length - 1)
        {
            errorMessage = "audioDataUrl is malformed.";
            return false;
        }

        string metadata = dataUrl.Substring(5, commaIndex - 5).Trim();
        if (!metadata.Contains(";base64", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "audioDataUrl must contain ';base64'.";
            return false;
        }

        int semicolonIndex = metadata.IndexOf(';');
        string parsedMimeType = semicolonIndex > 0
            ? metadata.Substring(0, semicolonIndex).Trim()
            : "";
        if (string.IsNullOrWhiteSpace(parsedMimeType))
        {
            parsedMimeType = "audio/mpeg";
        }

        string parsedBase64Data = dataUrl[(commaIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(parsedBase64Data))
        {
            errorMessage = "audioDataUrl does not contain base64 data.";
            return false;
        }

        mimeType = parsedMimeType;
        base64Data = parsedBase64Data;
        return true;
    }

    private static bool TryDecodeBase64(string encodedPayload, out byte[] decodedBytes)
    {
        decodedBytes = Array.Empty<byte>();
        try
        {
            decodedBytes = Convert.FromBase64String(encodedPayload);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryBuildNanobananaRequestJson(
        string operationName,
        JsonElement requestPayload,
        out string requestJson,
        out string errorMessage)
    {
        requestJson = "{}";
        errorMessage = "";

        if (requestPayload.ValueKind != JsonValueKind.Object)
        {
            errorMessage = "request must be a JSON object.";
            return false;
        }

        if (requestPayload.TryGetProperty("contents", out JsonElement contentsElement) &&
            contentsElement.ValueKind == JsonValueKind.Array)
        {
            requestJson = requestPayload.GetRawText();
            return true;
        }

        string prompt = GetStringOrEmpty(requestPayload, "prompt").Trim();
        bool hasInlineImage = TryReadNanobananaInlineImagePart(
            requestPayload,
            out string inlineImageMimeType,
            out string inlineImageData,
            out string inlineImageError);
        if (!string.IsNullOrWhiteSpace(inlineImageError))
        {
            errorMessage = inlineImageError;
            return false;
        }

        if (string.Equals(operationName, "edit", StringComparison.OrdinalIgnoreCase) && !hasInlineImage)
        {
            errorMessage = "Edit requests require imageBase64 or imageDataUrl unless request.contents is provided.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(prompt) && !hasInlineImage)
        {
            errorMessage = "request must include prompt or image input, or provide request.contents.";
            return false;
        }

        var requestWriterBuffer = new ArrayBufferWriter<byte>(2048);
        using (var writer = new Utf8JsonWriter(requestWriterBuffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("contents");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WritePropertyName("parts");
            writer.WriteStartArray();

            if (!string.IsNullOrWhiteSpace(prompt))
            {
                writer.WriteStartObject();
                writer.WriteString("text", prompt);
                writer.WriteEndObject();
            }

            if (hasInlineImage)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("inlineData");
                writer.WriteStartObject();
                writer.WriteString("mimeType", inlineImageMimeType);
                writer.WriteString("data", inlineImageData);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();

            WriteNanobananaOptionalRequestProperty(writer, requestPayload, "generationConfig");
            WriteNanobananaOptionalRequestProperty(writer, requestPayload, "safetySettings");
            WriteNanobananaOptionalRequestProperty(writer, requestPayload, "systemInstruction");
            WriteNanobananaOptionalRequestProperty(writer, requestPayload, "tools");
            WriteNanobananaOptionalRequestProperty(writer, requestPayload, "toolConfig");
            WriteNanobananaOptionalRequestProperty(writer, requestPayload, "cachedContent");

            writer.WriteEndObject();
        }

        requestJson = Encoding.UTF8.GetString(requestWriterBuffer.WrittenSpan);
        return true;
    }

    private static void WriteNanobananaOptionalRequestProperty(
        Utf8JsonWriter writer,
        JsonElement requestPayload,
        string propertyName)
    {
        if (!requestPayload.TryGetProperty(propertyName, out JsonElement propertyElement))
        {
            return;
        }

        writer.WritePropertyName(propertyName);
        propertyElement.WriteTo(writer);
    }

    private static bool TryReadNanobananaInlineImagePart(
        JsonElement requestPayload,
        out string mimeType,
        out string base64Data,
        out string errorMessage)
    {
        mimeType = "";
        base64Data = "";
        errorMessage = "";

        string imageBase64 = GetStringOrEmpty(requestPayload, "imageBase64").Trim();
        if (!string.IsNullOrWhiteSpace(imageBase64))
        {
            mimeType = GetStringOrEmpty(requestPayload, "mimeType").Trim();
            if (string.IsNullOrWhiteSpace(mimeType))
            {
                mimeType = "image/png";
            }

            base64Data = imageBase64;
            return true;
        }

        string imageDataUrl = GetStringOrEmpty(requestPayload, "imageDataUrl").Trim();
        if (string.IsNullOrWhiteSpace(imageDataUrl))
        {
            return false;
        }

        if (!TryParseNanobananaImageDataUrl(imageDataUrl, out string parsedMimeType, out string parsedBase64Data, out string parseError))
        {
            errorMessage = parseError;
            return false;
        }

        mimeType = parsedMimeType;
        base64Data = parsedBase64Data;
        return true;
    }

    private static bool TryParseNanobananaImageDataUrl(
        string imageDataUrl,
        out string mimeType,
        out string base64Data,
        out string errorMessage)
    {
        mimeType = "";
        base64Data = "";
        errorMessage = "";

        if (!imageDataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "imageDataUrl must start with 'data:'.";
            return false;
        }

        int commaIndex = imageDataUrl.IndexOf(',');
        if (commaIndex <= 5 || commaIndex >= imageDataUrl.Length - 1)
        {
            errorMessage = "imageDataUrl is malformed.";
            return false;
        }

        string metadata = imageDataUrl.Substring(5, commaIndex - 5).Trim();
        if (!metadata.Contains(";base64", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "imageDataUrl must contain ';base64'.";
            return false;
        }

        int semicolonIndex = metadata.IndexOf(';');
        string parsedMimeType = semicolonIndex > 0
            ? metadata.Substring(0, semicolonIndex).Trim()
            : "";
        if (string.IsNullOrWhiteSpace(parsedMimeType))
        {
            parsedMimeType = "image/png";
        }

        string parsedBase64Data = imageDataUrl[(commaIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(parsedBase64Data))
        {
            errorMessage = "imageDataUrl does not contain base64 data.";
            return false;
        }

        mimeType = parsedMimeType;
        base64Data = parsedBase64Data;
        return true;
    }

    private bool TryResolveActiveAssetsRoot(out string assetsRoot, out string errorMessage)
    {
        assetsRoot = "";
        errorMessage = "";

        if (string.IsNullOrWhiteSpace(_activeDbRoot))
        {
            errorMessage = "No active project. Call derpdoc.project.open first.";
            return false;
        }

        if (DocProjectPaths.TryGetGameRootFromDbRoot(_activeDbRoot, out string gameRoot))
        {
            assetsRoot = Path.Combine(gameRoot, "Assets");
        }
        else
        {
            assetsRoot = Path.Combine(_activeDbRoot, "Assets");
        }

        try
        {
            Directory.CreateDirectory(assetsRoot);
        }
        catch (Exception ex)
        {
            errorMessage = "Failed to prepare Assets directory: " + ex.Message;
            return false;
        }

        return true;
    }

    private static bool TryAssignTextureAssetCell(
        DocProject project,
        string tableId,
        string rowId,
        string columnId,
        int variantId,
        string assetPath,
        out string errorMessage)
    {
        errorMessage = "";

        DocTable? table = FindTable(project, tableId);
        if (table == null)
        {
            errorMessage = "Table not found: " + tableId + ".";
            return false;
        }

        DocColumn? column = FindColumn(table, columnId);
        if (column == null)
        {
            errorMessage = "Column not found: " + columnId + ".";
            return false;
        }

        if (column.Kind != DocColumnKind.TextureAsset)
        {
            errorMessage = "Column '" + columnId + "' must be a TextureAsset column.";
            return false;
        }

        if (!HasTableVariant(table, variantId))
        {
            errorMessage = "Variant '" + variantId.ToString(CultureInfo.InvariantCulture) + "' not found.";
            return false;
        }

        if (variantId != DocTableVariant.BaseVariantId && table.IsDerived)
        {
            errorMessage = "Variant row update is not supported for derived tables.";
            return false;
        }

        DocCellValue normalizedCellValue = DocCellValueNormalizer.NormalizeForColumn(column, DocCellValue.Text(assetPath));
        if (variantId == DocTableVariant.BaseVariantId)
        {
            DocRow? row = FindRow(table, rowId);
            if (row == null)
            {
                errorMessage = "Row not found: " + rowId + ".";
                return false;
            }

            row.SetCell(columnId, normalizedCellValue);
            return true;
        }

        DocTableVariantDelta variantDelta = GetOrCreateVariantDelta(table, variantId);
        DocRow? variantRow = FindVariantRow(table, variantDelta, rowId, out bool rowIsAdded, out bool rowIsDeletedBase);
        if (variantRow == null || rowIsDeletedBase)
        {
            errorMessage = "Row not found in variant '" + variantId.ToString(CultureInfo.InvariantCulture) + "': " + rowId + ".";
            return false;
        }

        if (rowIsAdded)
        {
            variantRow.SetCell(columnId, normalizedCellValue);
            return true;
        }

        int existingOverrideIndex = FindCellOverrideIndex(variantDelta, rowId, columnId);
        if (existingOverrideIndex >= 0)
        {
            variantDelta.CellOverrides[existingOverrideIndex].Value = normalizedCellValue.Clone();
        }
        else
        {
            variantDelta.CellOverrides.Add(new DocTableCellOverride
            {
                RowId = rowId,
                ColumnId = columnId,
                Value = normalizedCellValue.Clone(),
            });
        }

        return true;
    }

    private static bool TryAssignAudioAssetCell(
        DocProject project,
        string tableId,
        string rowId,
        string columnId,
        int variantId,
        string assetPath,
        out string errorMessage)
    {
        errorMessage = "";

        DocTable? table = FindTable(project, tableId);
        if (table == null)
        {
            errorMessage = "Table not found: " + tableId + ".";
            return false;
        }

        DocColumn? column = FindColumn(table, columnId);
        if (column == null)
        {
            errorMessage = "Column not found: " + columnId + ".";
            return false;
        }

        if (column.Kind != DocColumnKind.AudioAsset)
        {
            errorMessage = "Column '" + columnId + "' must be an AudioAsset column.";
            return false;
        }

        if (!HasTableVariant(table, variantId))
        {
            errorMessage = "Variant '" + variantId.ToString(CultureInfo.InvariantCulture) + "' not found.";
            return false;
        }

        if (variantId != DocTableVariant.BaseVariantId && table.IsDerived)
        {
            errorMessage = "Variant row update is not supported for derived tables.";
            return false;
        }

        DocCellValue normalizedCellValue = DocCellValueNormalizer.NormalizeForColumn(column, DocCellValue.Text(assetPath));
        if (variantId == DocTableVariant.BaseVariantId)
        {
            DocRow? row = FindRow(table, rowId);
            if (row == null)
            {
                errorMessage = "Row not found: " + rowId + ".";
                return false;
            }

            row.SetCell(columnId, normalizedCellValue);
            return true;
        }

        DocTableVariantDelta variantDelta = GetOrCreateVariantDelta(table, variantId);
        DocRow? variantRow = FindVariantRow(table, variantDelta, rowId, out bool rowIsAdded, out bool rowIsDeletedBase);
        if (variantRow == null || rowIsDeletedBase)
        {
            errorMessage = "Row not found in variant '" + variantId.ToString(CultureInfo.InvariantCulture) + "': " + rowId + ".";
            return false;
        }

        if (rowIsAdded)
        {
            variantRow.SetCell(columnId, normalizedCellValue);
            return true;
        }

        int existingOverrideIndex = FindCellOverrideIndex(variantDelta, rowId, columnId);
        if (existingOverrideIndex >= 0)
        {
            variantDelta.CellOverrides[existingOverrideIndex].Value = normalizedCellValue.Clone();
        }
        else
        {
            variantDelta.CellOverrides.Add(new DocTableCellOverride
            {
                RowId = rowId,
                ColumnId = columnId,
                Value = normalizedCellValue.Clone(),
            });
        }

        return true;
    }

    private static string EnsurePngExtension(string relativePath)
    {
        return EnsureFileExtension(relativePath, ".png");
    }

    private static string EnsureFileExtension(string relativePath, string extension)
    {
        string normalized = relativePath.Trim();
        if (normalized.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (Path.HasExtension(normalized))
        {
            return normalized;
        }

        return normalized + extension;
    }

    private static string BuildNanobananaAutoFileName(JsonElement requestPayload)
    {
        string prompt = GetStringOrEmpty(requestPayload, "prompt");
        string promptFileStem = SanitizeFileStem(prompt);
        if (string.Equals(promptFileStem, "table", StringComparison.Ordinal))
        {
            promptFileStem = "image";
        }

        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture);
        string token = Guid.NewGuid().ToString("N")[..6];
        return promptFileStem + "_" + timestamp + "_" + token + ".png";
    }

    private static string BuildElevenLabsAutoFileName(JsonElement requestPayload)
    {
        string text = GetStringOrEmpty(requestPayload, "text");
        if (string.IsNullOrWhiteSpace(text))
        {
            text = "audio";
        }

        string textFileStem = SanitizeFileStem(text);
        if (string.Equals(textFileStem, "table", StringComparison.Ordinal))
        {
            textFileStem = "audio";
        }

        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture);
        string token = Guid.NewGuid().ToString("N")[..6];
        return textFileStem + "_" + timestamp + "_" + token + ".mp3";
    }

    private static bool TryNormalizeRelativeAssetPath(string path, out string normalizedRelativePath)
    {
        normalizedRelativePath = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        ReadOnlySpan<char> invalidFileNameCharacters = stackalloc char[0];
        char[] invalidCharsArray = Path.GetInvalidFileNameChars();
        if (invalidCharsArray.Length > 0)
        {
            invalidFileNameCharacters = invalidCharsArray;
        }

        string[] rawSegments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (rawSegments.Length <= 0)
        {
            return false;
        }

        var normalizedSegments = new List<string>(rawSegments.Length);
        for (int segmentIndex = 0; segmentIndex < rawSegments.Length; segmentIndex++)
        {
            string rawSegment = rawSegments[segmentIndex].Trim();
            if (string.IsNullOrWhiteSpace(rawSegment) ||
                string.Equals(rawSegment, ".", StringComparison.Ordinal) ||
                string.Equals(rawSegment, "..", StringComparison.Ordinal))
            {
                return false;
            }

            bool hasInvalidCharacter = false;
            for (int characterIndex = 0; characterIndex < rawSegment.Length; characterIndex++)
            {
                if (invalidFileNameCharacters.IndexOf(rawSegment[characterIndex]) >= 0)
                {
                    hasInvalidCharacter = true;
                    break;
                }
            }

            if (hasInvalidCharacter)
            {
                return false;
            }

            normalizedSegments.Add(rawSegment);
        }

        normalizedRelativePath = string.Join('/', normalizedSegments);
        return normalizedRelativePath.Length > 0;
    }

    private static bool TryBuildQueryRowsForVariant(
        DocProject sourceProject,
        DocTable table,
        int variantId,
        int offset,
        int limit,
        out List<object> rows,
        out string errorMessage)
    {
        rows = new List<object>();
        errorMessage = "";

        if (variantId == DocTableVariant.BaseVariantId)
        {
            rows = BuildQueryRows(table, offset, limit);
            return true;
        }

        if (!table.IsDerived)
        {
            rows = BuildQueryRowsFromVariantDelta(table, variantId, offset, limit);
            return true;
        }

        errorMessage = "Variants are not supported for derived tables.";
        return false;
    }

    private static List<object> BuildQueryRowsFromVariantDelta(
        DocTable table,
        int variantId,
        int offset,
        int limit)
    {
        if (!TryGetVariantDelta(table, variantId, out DocTableVariantDelta? variantDelta) || variantDelta == null)
        {
            return BuildQueryRows(table, offset, limit);
        }

        var overridesByRowId = BuildVariantOverrideLookup(variantDelta);
        var materializedRows = new List<DocRow>(table.Rows.Count + variantDelta.AddedRows.Count);
        for (int baseRowIndex = 0; baseRowIndex < table.Rows.Count; baseRowIndex++)
        {
            DocRow baseRow = table.Rows[baseRowIndex];
            if (variantDelta.DeletedBaseRowIds.Contains(baseRow.Id))
            {
                continue;
            }

            materializedRows.Add(baseRow);
        }

        for (int addedRowIndex = 0; addedRowIndex < variantDelta.AddedRows.Count; addedRowIndex++)
        {
            materializedRows.Add(variantDelta.AddedRows[addedRowIndex]);
        }

        offset = Math.Max(0, offset);
        limit = Math.Max(0, limit);

        int end = Math.Min(materializedRows.Count, offset + limit);
        var rows = new List<object>(Math.Max(0, end - offset));
        for (int rowIndex = offset; rowIndex < end; rowIndex++)
        {
            DocRow row = materializedRows[rowIndex];
            var cells = new Dictionary<string, object?>(table.Columns.Count);
            bool hasRowOverrides = overridesByRowId.TryGetValue(row.Id, out Dictionary<string, DocCellValue>? rowOverrides);
            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                DocColumn column = table.Columns[columnIndex];
                DocCellValue cellValue = row.GetCell(column);
                if (hasRowOverrides &&
                    rowOverrides != null &&
                    rowOverrides.TryGetValue(column.Id, out DocCellValue overrideCellValue))
                {
                    cellValue = overrideCellValue;
                }

                cells[column.Id] = FormatToolCellValue(column, cellValue);
            }

            rows.Add(new { id = row.Id, cells });
        }

        return rows;
    }

    private static Dictionary<string, Dictionary<string, DocCellValue>> BuildVariantOverrideLookup(DocTableVariantDelta variantDelta)
    {
        var overridesByRowId = new Dictionary<string, Dictionary<string, DocCellValue>>(StringComparer.Ordinal);
        for (int overrideIndex = 0; overrideIndex < variantDelta.CellOverrides.Count; overrideIndex++)
        {
            DocTableCellOverride cellOverride = variantDelta.CellOverrides[overrideIndex];
            if (!overridesByRowId.TryGetValue(cellOverride.RowId, out Dictionary<string, DocCellValue>? rowOverrides))
            {
                rowOverrides = new Dictionary<string, DocCellValue>(StringComparer.Ordinal);
                overridesByRowId[cellOverride.RowId] = rowOverrides;
            }

            rowOverrides[cellOverride.ColumnId] = cellOverride.Value;
        }

        return overridesByRowId;
    }

    // ── Shared parsing helpers ──

    private static void ParseFiltersFromArgs(JsonElement args, DocView view)
    {
        if (!args.TryGetProperty("filters", out var filtersEl) || filtersEl.ValueKind != JsonValueKind.Array)
            return;

        view.Filters.Clear();
        foreach (var fEl in filtersEl.EnumerateArray())
        {
            var filter = new DocViewFilter
            {
                ColumnId = fEl.TryGetProperty("columnId", out var cid) ? cid.GetString() ?? "" : "",
                Value = fEl.TryGetProperty("value", out var val) ? val.GetString() ?? "" : "",
            };
            if (fEl.TryGetProperty("op", out var opEl))
            {
                filter.Op = (opEl.GetString() ?? "").ToLowerInvariant() switch
                {
                    "equals" => DocViewFilterOp.Equals,
                    "notequals" => DocViewFilterOp.NotEquals,
                    "contains" => DocViewFilterOp.Contains,
                    "notcontains" => DocViewFilterOp.NotContains,
                    "greaterthan" => DocViewFilterOp.GreaterThan,
                    "lessthan" => DocViewFilterOp.LessThan,
                    "isempty" => DocViewFilterOp.IsEmpty,
                    "isnotempty" => DocViewFilterOp.IsNotEmpty,
                    _ => DocViewFilterOp.Contains,
                };
            }
            view.Filters.Add(filter);
        }
    }

    private static void ParseSortsFromArgs(JsonElement args, DocView view)
    {
        if (!args.TryGetProperty("sorts", out var sortsEl) || sortsEl.ValueKind != JsonValueKind.Array)
            return;

        view.Sorts.Clear();
        foreach (var sEl in sortsEl.EnumerateArray())
        {
            var sort = new DocViewSort
            {
                ColumnId = sEl.TryGetProperty("columnId", out var cid) ? cid.GetString() ?? "" : "",
                Descending = sEl.TryGetProperty("descending", out var desc) && desc.GetBoolean(),
            };
            view.Sorts.Add(sort);
        }
    }

    private static bool TryParseFolderScope(string? rawScope, out DocFolderScope scope)
    {
        scope = DocFolderScope.Tables;
        if (string.IsNullOrWhiteSpace(rawScope))
        {
            return false;
        }

        if (string.Equals(rawScope, "Tables", StringComparison.OrdinalIgnoreCase))
        {
            scope = DocFolderScope.Tables;
            return true;
        }

        if (string.Equals(rawScope, "Documents", StringComparison.OrdinalIgnoreCase))
        {
            scope = DocFolderScope.Documents;
            return true;
        }

        return false;
    }

    private static DocFolder? FindFolder(DocProject project, string folderId)
    {
        for (int folderIndex = 0; folderIndex < project.Folders.Count; folderIndex++)
        {
            if (string.Equals(project.Folders[folderIndex].Id, folderId, StringComparison.Ordinal))
            {
                return project.Folders[folderIndex];
            }
        }

        return null;
    }

    private static DocDocument? FindDocument(DocProject project, string documentId)
    {
        for (int i = 0; i < project.Documents.Count; i++)
        {
            if (string.Equals(project.Documents[i].Id, documentId, StringComparison.Ordinal) ||
                string.Equals(project.Documents[i].Title, documentId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(project.Documents[i].FileName, documentId, StringComparison.OrdinalIgnoreCase))
            {
                return project.Documents[i];
            }
        }
        return null;
    }

    private static int FindDocumentIndex(DocProject project, string documentId)
    {
        for (int documentIndex = 0; documentIndex < project.Documents.Count; documentIndex++)
        {
            var document = project.Documents[documentIndex];
            if (string.Equals(document.Id, documentId, StringComparison.Ordinal) ||
                string.Equals(document.Title, documentId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(document.FileName, documentId, StringComparison.OrdinalIgnoreCase))
            {
                return documentIndex;
            }
        }

        return -1;
    }

    private static DocBlock? FindBlock(DocDocument document, string blockId)
    {
        for (int i = 0; i < document.Blocks.Count; i++)
        {
            if (string.Equals(document.Blocks[i].Id, blockId, StringComparison.Ordinal))
            {
                return document.Blocks[i];
            }
        }
        return null;
    }

    private static int FindBlockIndex(DocDocument document, string blockId)
    {
        for (int blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            if (string.Equals(document.Blocks[blockIndex].Id, blockId, StringComparison.Ordinal))
            {
                return blockIndex;
            }
        }

        return -1;
    }

    private static bool HasView(DocTable table, string viewId)
    {
        for (int viewIndex = 0; viewIndex < table.Views.Count; viewIndex++)
        {
            if (string.Equals(table.Views[viewIndex].Id, viewId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void NormalizeDocumentBlockOrders(DocDocument document)
    {
        string nextOrder = FractionalIndex.Initial();
        for (int blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            document.Blocks[blockIndex].Order = nextOrder;
            nextOrder = FractionalIndex.After(nextOrder);
        }
    }

    private static string ComputeInsertedOrder(IReadOnlyList<DocBlock> blocks, int index)
    {
        string beforeOrder = index > 0 ? blocks[index - 1].Order : "";
        string afterOrder = index < blocks.Count ? blocks[index].Order : "";

        if (string.IsNullOrWhiteSpace(beforeOrder) && string.IsNullOrWhiteSpace(afterOrder))
        {
            return FractionalIndex.Initial();
        }

        if (string.IsNullOrWhiteSpace(beforeOrder))
        {
            return FractionalIndex.Before(afterOrder);
        }

        if (string.IsNullOrWhiteSpace(afterOrder))
        {
            return FractionalIndex.After(beforeOrder);
        }

        return FractionalIndex.Between(beforeOrder, afterOrder);
    }

    private static string SanitizeDocumentFileStem(string value)
    {
        string sanitized = SanitizeFileStem(value);
        if (string.Equals(sanitized, "table", StringComparison.Ordinal))
        {
            return "document";
        }

        return sanitized;
    }

    private static string MakeUniqueDocumentFileName(DocProject project, string baseFileName)
    {
        string candidate = baseFileName;
        int suffix = 2;
        while (HasDocumentFileName(project, candidate))
        {
            candidate = baseFileName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
            suffix++;
        }

        return candidate;
    }

    private static bool HasDocumentFileName(DocProject project, string fileName)
    {
        for (int documentIndex = 0; documentIndex < project.Documents.Count; documentIndex++)
        {
            if (string.Equals(project.Documents[documentIndex].FileName, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseBlockType(string? rawType, out DocBlockType blockType)
    {
        blockType = DocBlockType.Paragraph;
        if (string.IsNullOrWhiteSpace(rawType))
        {
            return false;
        }

        if (Enum.TryParse<DocBlockType>(rawType, ignoreCase: true, out blockType))
        {
            return true;
        }

        return false;
    }

    private static string? TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    private static string? GetArgStringOrNull(JsonElement args, string propertyName)
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(propertyName, out var prop))
        {
            return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
        }
        return null;
    }

    private static bool TryReadLegacyProjectVariantInfo(
        string projectJsonPath,
        out bool hasLegacyProjectVariants,
        out int legacyProjectVariantCount,
        out string error)
    {
        hasLegacyProjectVariants = false;
        legacyProjectVariantCount = 0;
        error = "";

        if (!File.Exists(projectJsonPath))
        {
            error = $"project.json not found: {projectJsonPath}";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(projectJsonPath));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "project.json root must be an object.";
                return false;
            }

            if (!root.TryGetProperty("variants", out JsonElement variantsElement))
            {
                return true;
            }

            hasLegacyProjectVariants = true;
            if (variantsElement.ValueKind == JsonValueKind.Array)
            {
                legacyProjectVariantCount = variantsElement.GetArrayLength();
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private bool TryLoadActiveProject(out DocProject project, out string errorJson)
    {
        project = null!;
        errorJson = "";
        TrySyncActiveDbRootFromUi();
        if (string.IsNullOrWhiteSpace(_activeDbRoot))
        {
            errorJson = JsonSerializer.Serialize(new { error = "No active project. Call derpdoc.project.open first." });
            return false;
        }

        project = ProjectLoader.Load(_activeDbRoot);
        try
        {
            SchemaLinkedTableSynchronizer.Synchronize(project);
        }
        catch (Exception ex)
        {
            errorJson = JsonSerializer.Serialize(new { error = ex.Message });
            return false;
        }

        return true;
    }

    private void TrySyncActiveDbRootFromUi()
    {
        if (!_options.FollowUiActiveProject)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_activeDbRoot) && !_activeDbRootIsFromUi)
        {
            return;
        }

        if (!DocActiveProjectStateFile.TryReadDbRoot(_options.WorkspaceRoot, out var dbRoot)
            && !DocActiveProjectStateFile.TryReadDbRootSearchingUp(_options.WorkspaceRoot, out dbRoot, out _))
        {
            return;
        }

        _activeDbRoot = dbRoot;
        _activeDbRootIsFromUi = true;
    }

    private void MarkExternalChange()
    {
        if (string.IsNullOrWhiteSpace(_activeDbRoot))
        {
            return;
        }

        try
        {
            DocExternalChangeSignalFile.Touch(_activeDbRoot);
        }
        catch
        {
            // Best-effort: external change notification should not fail the tool call.
        }
    }

    private void SaveActiveProjectAndNotify(DocProject project)
    {
        ProjectSerializer.Save(project, _activeDbRoot);
        MarkExternalChange();
        TryAutoExportLiveBinary(project);
    }

    private void TryAutoExportLiveBinary(DocProject project)
    {
        if (!_options.AutoLiveExportOnMutation || string.IsNullOrWhiteSpace(_activeDbRoot))
        {
            return;
        }

        if (!HasEnabledExportTable(project))
        {
            return;
        }

        try
        {
            string? gameRoot = null;
            if (DocProjectPaths.TryGetGameRootFromDbRoot(_activeDbRoot, out var inferredGameRoot))
            {
                gameRoot = inferredGameRoot;
            }

            string binaryPath = DocProjectPaths.ResolveDefaultBinaryPath(_activeDbRoot, gameRoot);
            string liveBinaryPath = DocProjectPaths.ResolveDefaultLiveBinaryPath(_activeDbRoot);

            var options = new ExportPipelineOptions
            {
                GeneratedOutputDirectory = "",
                BinaryOutputPath = binaryPath,
                LiveBinaryOutputPath = liveBinaryPath,
                WriteManifest = false,
            };

            var pipeline = new DocExportPipeline();
            _ = pipeline.Export(project, options);
        }
        catch
        {
            // Best-effort auto live export: write operations should still succeed.
        }
    }

    private static bool HasEnabledExportTable(DocProject project)
    {
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            if (project.Tables[tableIndex].ExportConfig?.Enabled == true)
            {
                return true;
            }
        }

        return false;
    }

    private string ResolvePathWithinWorkspace(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(_options.WorkspaceRoot, path));
    }

    private static DocTable? FindTable(DocProject project, string tableId)
    {
        for (int i = 0; i < project.Tables.Count; i++)
        {
            if (string.Equals(project.Tables[i].Id, tableId, StringComparison.Ordinal))
            {
                return project.Tables[i];
            }
        }
        return null;
    }

    private static int FindTableIndex(DocProject project, string tableId)
    {
        for (int i = 0; i < project.Tables.Count; i++)
        {
            if (string.Equals(project.Tables[i].Id, tableId, StringComparison.Ordinal))
            {
                return i;
            }
        }
        return -1;
    }

    private static DocColumn? FindColumn(DocTable table, string columnId)
    {
        for (int i = 0; i < table.Columns.Count; i++)
        {
            var c = table.Columns[i];
            if (string.Equals(c.Id, columnId, StringComparison.Ordinal))
            {
                return c;
            }
        }
        return null;
    }

    private static int FindColumnIndex(DocTable table, string columnId)
    {
        for (int i = 0; i < table.Columns.Count; i++)
        {
            if (string.Equals(table.Columns[i].Id, columnId, StringComparison.Ordinal))
            {
                return i;
            }
        }
        return -1;
    }

    private static DocRow? FindRow(DocTable table, string rowId)
    {
        for (int i = 0; i < table.Rows.Count; i++)
        {
            if (string.Equals(table.Rows[i].Id, rowId, StringComparison.Ordinal))
            {
                return table.Rows[i];
            }
        }
        return null;
    }

    private static int FindRowIndex(DocTable table, string rowId)
    {
        for (int i = 0; i < table.Rows.Count; i++)
        {
            if (string.Equals(table.Rows[i].Id, rowId, StringComparison.Ordinal))
            {
                return i;
            }
        }
        return -1;
    }

    private static bool TryParseColumnKind(string raw, out DocColumnKind kind)
    {
        kind = DocColumnKind.Text;
        if (string.Equals(raw, "Text", StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.Text;
            return true;
        }
        if (string.Equals(raw, "Number", StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.Number;
            return true;
        }
        if (string.Equals(raw, "Checkbox", StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.Checkbox;
            return true;
        }
        if (string.Equals(raw, "Select", StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.Select;
            return true;
        }
        if (string.Equals(raw, "Formula", StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.Formula;
            return true;
        }
        if (string.Equals(raw, "Relation", StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.Relation;
            return true;
        }
        if (string.Equals(raw, "Subtable", StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.Subtable;
            return true;
        }
        if (string.Equals(raw, "Spline", StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.Spline;
            return true;
        }
        if (string.Equals(raw, "TextureAsset", StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.TextureAsset;
            return true;
        }
        if (string.Equals(raw, "MeshAsset", StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.MeshAsset;
            return true;
        }
        if (string.Equals(raw, "AudioAsset", StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.AudioAsset;
            return true;
        }
        if (string.Equals(raw, "UiAsset", StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.UiAsset;
            return true;
        }
        if (string.Equals(raw, "Vec2", StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.Vec2;
            return true;
        }
        if (string.Equals(raw, "Vec3", StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.Vec3;
            return true;
        }
        if (string.Equals(raw, "Vec4", StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.Vec4;
            return true;
        }
        if (string.Equals(raw, "Color", StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.Color;
            return true;
        }
        return false;
    }

    private static bool TryReadFormulaEvalScopes(JsonElement args, out DocFormulaEvalScope formulaEvalScopes)
    {
        formulaEvalScopes = DocFormulaEvalScope.None;
        if (args.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (args.TryGetProperty("formulaEvalScopes", out var scopesElement))
        {
            if (scopesElement.ValueKind == JsonValueKind.Null)
            {
                return true;
            }

            if (scopesElement.ValueKind == JsonValueKind.String)
            {
                string rawScopes = scopesElement.GetString() ?? "";
                if (TryParseFormulaEvalScopes(rawScopes, out formulaEvalScopes))
                {
                    return true;
                }
            }
        }

        // Backward compatibility alias
        if (args.TryGetProperty("livePreviewPriority", out var legacyPriorityElement) &&
            legacyPriorityElement.ValueKind == JsonValueKind.String)
        {
            string legacyValue = legacyPriorityElement.GetString() ?? "";
            if (TryParseFormulaEvalScopes(legacyValue, out formulaEvalScopes))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseFormulaEvalScopes(string rawValue, out DocFormulaEvalScope formulaEvalScopes)
    {
        if (Enum.TryParse<DocFormulaEvalScope>(rawValue, ignoreCase: true, out formulaEvalScopes))
        {
            return true;
        }

        if (string.Equals(rawValue, "ChartImmediate", StringComparison.OrdinalIgnoreCase))
        {
            formulaEvalScopes = DocFormulaEvalScope.Interactive;
            return true;
        }

        formulaEvalScopes = DocFormulaEvalScope.None;
        return false;
    }

    private static string SanitizeFileStem(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "table";
        }

        Span<char> buffer = stackalloc char[Math.Min(64, value.Length)];
        int w = 0;
        for (int i = 0; i < value.Length && w < buffer.Length; i++)
        {
            char c = value[i];
            if (c >= 'A' && c <= 'Z')
            {
                buffer[w++] = (char)(c + 32);
            }
            else if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                buffer[w++] = c;
            }
            else if (c == '_' || c == '-')
            {
                buffer[w++] = c;
            }
            else if (c == ' ')
            {
                buffer[w++] = '_';
            }
        }

        if (w == 0)
        {
            return "table";
        }

        return new string(buffer.Slice(0, w));
    }

    private static DocView? ResolveOrCreateNodeGraphView(
        DocTable table,
        string requestedViewId,
        string requestedViewName,
        out bool createdView)
    {
        createdView = false;
        if (!string.IsNullOrWhiteSpace(requestedViewId))
        {
            return FindViewById(table, requestedViewId);
        }

        DocView? existingNodeGraphView = ResolveExistingNodeGraphView(table, "");
        if (existingNodeGraphView != null)
        {
            return existingNodeGraphView;
        }

        var created = new DocView
        {
            Name = string.IsNullOrWhiteSpace(requestedViewName) ? "Node Graph" : requestedViewName,
            Type = DocViewType.Custom,
            CustomRendererId = NodeGraphRendererId,
        };
        table.Views.Add(created);
        createdView = true;
        return created;
    }

    private static DocView? ResolveExistingNodeGraphView(DocTable table, string requestedViewId)
    {
        if (!string.IsNullOrWhiteSpace(requestedViewId))
        {
            DocView? requested = FindViewById(table, requestedViewId);
            if (requested == null)
            {
                return null;
            }

            if (requested.Type == DocViewType.Custom &&
                string.Equals(requested.CustomRendererId, NodeGraphRendererId, StringComparison.Ordinal))
            {
                return requested;
            }

            return null;
        }

        for (int viewIndex = 0; viewIndex < table.Views.Count; viewIndex++)
        {
            DocView candidate = table.Views[viewIndex];
            if (candidate.Type == DocViewType.Custom &&
                string.Equals(candidate.CustomRendererId, NodeGraphRendererId, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private static DocView? FindViewById(DocTable table, string viewId)
    {
        for (int viewIndex = 0; viewIndex < table.Views.Count; viewIndex++)
        {
            DocView view = table.Views[viewIndex];
            if (string.Equals(view.Id, viewId, StringComparison.Ordinal))
            {
                return view;
            }
        }

        return null;
    }

    private static string BuildNodeGraphViewKey(DocTable table, DocView view)
    {
        return NodeGraphSettingsNamespace + ":" + table.Id + ":" + view.Id;
    }

    private static NodeGraphViewSettingsPayload ReadNodeGraphViewSettings(
        DocProject project,
        DocTable table,
        DocView view)
    {
        string key = BuildNodeGraphViewKey(table, view);
        if (project.PluginSettingsByKey.TryGetValue(key, out string? serializedSettings) &&
            !string.IsNullOrWhiteSpace(serializedSettings))
        {
            try
            {
                NodeGraphViewSettingsPayload? parsedSettings = JsonSerializer.Deserialize<NodeGraphViewSettingsPayload>(serializedSettings);
                if (parsedSettings != null)
                {
                    NormalizeNodeGraphViewSettings(parsedSettings);
                    return parsedSettings;
                }
            }
            catch
            {
                // Fall through to defaults.
            }
        }

        var createdSettings = new NodeGraphViewSettingsPayload();
        NormalizeNodeGraphViewSettings(createdSettings);
        return createdSettings;
    }

    private static void WriteNodeGraphViewSettings(
        DocProject project,
        DocTable table,
        DocView view,
        NodeGraphViewSettingsPayload settings)
    {
        NormalizeNodeGraphViewSettings(settings);
        string key = BuildNodeGraphViewKey(table, view);
        project.PluginSettingsByKey[key] = JsonSerializer.Serialize(settings);
    }

    private static void NormalizeNodeGraphViewSettings(NodeGraphViewSettingsPayload settings)
    {
        settings.TypeColumnId ??= "";
        settings.PositionColumnId ??= "";
        settings.TitleColumnId ??= "";
        settings.ExecutionOutputColumnId ??= "";
        settings.EdgeSubtableColumnId ??= "";
        settings.TypeLayouts ??= new List<NodeGraphTypeLayoutPayload>();

        for (int layoutIndex = settings.TypeLayouts.Count - 1; layoutIndex >= 0; layoutIndex--)
        {
            NodeGraphTypeLayoutPayload layout = settings.TypeLayouts[layoutIndex];
            if (layout == null || string.IsNullOrWhiteSpace(layout.TypeName))
            {
                settings.TypeLayouts.RemoveAt(layoutIndex);
                continue;
            }

            layout.TypeName = layout.TypeName.Trim();
            layout.NodeWidth = ClampNodeGraphNodeWidth(layout.NodeWidth);
            layout.Fields ??= new List<NodeGraphFieldLayoutPayload>();

            var seenColumnIds = new HashSet<string>(StringComparer.Ordinal);
            for (int fieldIndex = layout.Fields.Count - 1; fieldIndex >= 0; fieldIndex--)
            {
                NodeGraphFieldLayoutPayload field = layout.Fields[fieldIndex];
                if (field == null || string.IsNullOrWhiteSpace(field.ColumnId))
                {
                    layout.Fields.RemoveAt(fieldIndex);
                    continue;
                }

                if (!seenColumnIds.Add(field.ColumnId))
                {
                    layout.Fields.RemoveAt(fieldIndex);
                    continue;
                }

                if (!TryNormalizeNodeGraphFieldMode(field.Mode, out string normalizedMode))
                {
                    normalizedMode = "Setting";
                }
                field.Mode = normalizedMode;
            }
        }
    }

    private static NodeGraphResolvedSchema ResolveNodeGraphSchema(
        DocProject project,
        DocTable table,
        NodeGraphViewSettingsPayload settings)
    {
        DocColumn? typeColumn = FindColumn(table, settings.TypeColumnId) ??
                                FindColumnByNameAndKind(table, NodeGraphDefaultTypeColumnName, DocColumnKind.Select);
        DocColumn? positionColumn = FindColumn(table, settings.PositionColumnId) ??
                                    FindColumnByNameAndKind(table, NodeGraphDefaultPositionColumnName, DocColumnKind.Vec2);
        DocColumn? titleColumn = FindColumn(table, settings.TitleColumnId) ??
                                 FindColumnByNameAndKind(table, NodeGraphDefaultTitleColumnName, DocColumnKind.Text);
        DocColumn? executionOutputColumn = FindColumn(table, settings.ExecutionOutputColumnId) ??
                                           FindColumnByNameAndKind(table, NodeGraphDefaultExecutionNextColumnName, DocColumnKind.Relation);
        DocColumn? edgeSubtableColumn = FindColumn(table, settings.EdgeSubtableColumnId) ??
                                        FindColumnByNameAndKind(table, NodeGraphDefaultEdgesColumnName, DocColumnKind.Subtable);

        DocTable? edgeTable = null;
        if (edgeSubtableColumn != null && !string.IsNullOrWhiteSpace(edgeSubtableColumn.SubtableId))
        {
            edgeTable = FindTable(project, edgeSubtableColumn.SubtableId);
        }

        if (edgeTable == null)
        {
            edgeTable = FindNodeGraphEdgeTableCandidate(project, table);
        }

        DocColumn? edgeFromNodeColumn = edgeTable != null
            ? FindColumnByNameAndKind(edgeTable, NodeGraphDefaultFromNodeColumnName, DocColumnKind.Relation)
            : null;
        DocColumn? edgeFromPinColumn = edgeTable != null
            ? FindColumnByNameAndKind(edgeTable, NodeGraphDefaultFromPinColumnName, DocColumnKind.Text)
            : null;
        DocColumn? edgeToNodeColumn = edgeTable != null
            ? FindColumnByNameAndKind(edgeTable, NodeGraphDefaultToNodeColumnName, DocColumnKind.Relation)
            : null;
        DocColumn? edgeToPinColumn = edgeTable != null
            ? FindColumnByNameAndKind(edgeTable, NodeGraphDefaultToPinColumnName, DocColumnKind.Text)
            : null;

        return new NodeGraphResolvedSchema
        {
            TypeColumn = typeColumn,
            PositionColumn = positionColumn,
            TitleColumn = titleColumn,
            ExecutionOutputColumn = executionOutputColumn,
            EdgeSubtableColumn = edgeSubtableColumn,
            EdgeTable = edgeTable,
            EdgeFromNodeColumn = edgeFromNodeColumn,
            EdgeFromPinColumn = edgeFromPinColumn,
            EdgeToNodeColumn = edgeToNodeColumn,
            EdgeToPinColumn = edgeToPinColumn,
        };
    }

    private static bool HasRequiredNodeGraphSchema(NodeGraphResolvedSchema schema)
    {
        return schema.TypeColumn != null &&
               schema.PositionColumn != null &&
               schema.ExecutionOutputColumn != null &&
               schema.EdgeSubtableColumn != null &&
               schema.EdgeTable != null &&
               schema.EdgeFromNodeColumn != null &&
               schema.EdgeFromPinColumn != null &&
               schema.EdgeToNodeColumn != null &&
               schema.EdgeToPinColumn != null;
    }

    private static bool EnsureNodeGraphSchemaScaffold(
        DocProject project,
        DocTable table,
        NodeGraphViewSettingsPayload settings,
        out NodeGraphResolvedSchema resolvedSchema)
    {
        bool updated = false;
        NodeGraphResolvedSchema schema = ResolveNodeGraphSchema(project, table, settings);

        DocColumn? typeColumn = schema.TypeColumn;
        if (typeColumn == null)
        {
            typeColumn = new DocColumn
            {
                Name = NodeGraphDefaultTypeColumnName,
                Kind = DocColumnKind.Select,
                Width = 140f,
                Options = new List<string> { NodeGraphDefaultTypeOption },
            };
            AddColumnWithDefaults(table, typeColumn);
            updated = true;
        }
        else
        {
            typeColumn.Options ??= new List<string>();
            if (!ContainsOption(typeColumn.Options, NodeGraphDefaultTypeOption))
            {
                typeColumn.Options.Insert(0, NodeGraphDefaultTypeOption);
                updated = true;
            }
        }

        DocColumn? positionColumn = schema.PositionColumn;
        if (positionColumn == null)
        {
            positionColumn = new DocColumn
            {
                Name = NodeGraphDefaultPositionColumnName,
                Kind = DocColumnKind.Vec2,
                ColumnTypeId = DocColumnTypeIds.Vec2Fixed64,
                Width = 130f,
            };
            AddColumnWithDefaults(table, positionColumn);
            updated = true;
        }

        DocColumn? titleColumn = schema.TitleColumn;
        if (titleColumn == null)
        {
            titleColumn = new DocColumn
            {
                Name = NodeGraphDefaultTitleColumnName,
                Kind = DocColumnKind.Text,
                Width = 180f,
            };
            AddColumnWithDefaults(table, titleColumn);
            updated = true;
        }

        DocColumn? executionOutputColumn = schema.ExecutionOutputColumn;
        if (executionOutputColumn == null)
        {
            executionOutputColumn = new DocColumn
            {
                Name = NodeGraphDefaultExecutionNextColumnName,
                Kind = DocColumnKind.Relation,
                RelationTargetMode = DocRelationTargetMode.SelfTable,
                RelationTableId = table.Id,
                RelationDisplayColumnId = titleColumn?.Id ?? typeColumn?.Id,
                Width = 130f,
                IsHidden = true,
            };
            AddColumnWithDefaults(table, executionOutputColumn);
            updated = true;
        }
        else
        {
            if (executionOutputColumn.Kind == DocColumnKind.Relation)
            {
                if (executionOutputColumn.RelationTargetMode != DocRelationTargetMode.SelfTable)
                {
                    executionOutputColumn.RelationTargetMode = DocRelationTargetMode.SelfTable;
                    updated = true;
                }
                if (!string.Equals(executionOutputColumn.RelationTableId, table.Id, StringComparison.Ordinal))
                {
                    executionOutputColumn.RelationTableId = table.Id;
                    updated = true;
                }
                string desiredDisplayColumnId = titleColumn?.Id ?? typeColumn?.Id ?? "";
                if (!string.IsNullOrWhiteSpace(desiredDisplayColumnId) &&
                    !string.Equals(executionOutputColumn.RelationDisplayColumnId, desiredDisplayColumnId, StringComparison.Ordinal))
                {
                    executionOutputColumn.RelationDisplayColumnId = desiredDisplayColumnId;
                    updated = true;
                }
            }
            if (!executionOutputColumn.IsHidden)
            {
                executionOutputColumn.IsHidden = true;
                updated = true;
            }
        }

        DocTable? edgeTable = schema.EdgeTable;
        if (edgeTable == null)
        {
            int nextSubtableIndex = project.Tables.Count + 1;
            edgeTable = new DocTable
            {
                Name = table.Name + "_" + NodeGraphDefaultEdgesColumnName,
                FileName = "subtable" + nextSubtableIndex.ToString(CultureInfo.InvariantCulture),
                ParentTableId = table.Id,
            };
            project.Tables.Add(edgeTable);
            updated = true;
        }

        DocColumn? parentRowColumn = FindColumnByNameAndKind(edgeTable, NodeGraphDefaultParentRowColumnName, DocColumnKind.Text);
        if (parentRowColumn == null)
        {
            parentRowColumn = new DocColumn
            {
                Name = NodeGraphDefaultParentRowColumnName,
                Kind = DocColumnKind.Text,
                IsHidden = true,
                Width = 120f,
            };
            AddColumnWithDefaults(edgeTable, parentRowColumn);
            updated = true;
        }
        else if (!parentRowColumn.IsHidden)
        {
            parentRowColumn.IsHidden = true;
            updated = true;
        }

        if (!string.Equals(edgeTable.ParentTableId, table.Id, StringComparison.Ordinal))
        {
            edgeTable.ParentTableId = table.Id;
            updated = true;
        }

        if (!string.Equals(edgeTable.ParentRowColumnId, parentRowColumn.Id, StringComparison.Ordinal))
        {
            edgeTable.ParentRowColumnId = parentRowColumn.Id;
            updated = true;
        }

        DocColumn? fromNodeColumn = FindColumnByNameAndKind(edgeTable, NodeGraphDefaultFromNodeColumnName, DocColumnKind.Relation);
        if (fromNodeColumn == null)
        {
            fromNodeColumn = new DocColumn
            {
                Name = NodeGraphDefaultFromNodeColumnName,
                Kind = DocColumnKind.Relation,
                RelationTargetMode = DocRelationTargetMode.SelfTable,
                RelationTableId = table.Id,
                RelationDisplayColumnId = titleColumn?.Id ?? typeColumn?.Id,
                Width = 140f,
            };
            AddColumnWithDefaults(edgeTable, fromNodeColumn);
            updated = true;
        }
        else
        {
            if (fromNodeColumn.RelationTargetMode != DocRelationTargetMode.SelfTable)
            {
                fromNodeColumn.RelationTargetMode = DocRelationTargetMode.SelfTable;
                updated = true;
            }
            if (!string.Equals(fromNodeColumn.RelationTableId, table.Id, StringComparison.Ordinal))
            {
                fromNodeColumn.RelationTableId = table.Id;
                updated = true;
            }
        }

        DocColumn? fromPinColumn = FindColumnByNameAndKind(edgeTable, NodeGraphDefaultFromPinColumnName, DocColumnKind.Text);
        if (fromPinColumn == null)
        {
            fromPinColumn = new DocColumn
            {
                Name = NodeGraphDefaultFromPinColumnName,
                Kind = DocColumnKind.Text,
                Width = 110f,
            };
            AddColumnWithDefaults(edgeTable, fromPinColumn);
            updated = true;
        }

        DocColumn? toNodeColumn = FindColumnByNameAndKind(edgeTable, NodeGraphDefaultToNodeColumnName, DocColumnKind.Relation);
        if (toNodeColumn == null)
        {
            toNodeColumn = new DocColumn
            {
                Name = NodeGraphDefaultToNodeColumnName,
                Kind = DocColumnKind.Relation,
                RelationTargetMode = DocRelationTargetMode.SelfTable,
                RelationTableId = table.Id,
                RelationDisplayColumnId = titleColumn?.Id ?? typeColumn?.Id,
                Width = 140f,
            };
            AddColumnWithDefaults(edgeTable, toNodeColumn);
            updated = true;
        }
        else
        {
            if (toNodeColumn.RelationTargetMode != DocRelationTargetMode.SelfTable)
            {
                toNodeColumn.RelationTargetMode = DocRelationTargetMode.SelfTable;
                updated = true;
            }
            if (!string.Equals(toNodeColumn.RelationTableId, table.Id, StringComparison.Ordinal))
            {
                toNodeColumn.RelationTableId = table.Id;
                updated = true;
            }
        }

        DocColumn? toPinColumn = FindColumnByNameAndKind(edgeTable, NodeGraphDefaultToPinColumnName, DocColumnKind.Text);
        if (toPinColumn == null)
        {
            toPinColumn = new DocColumn
            {
                Name = NodeGraphDefaultToPinColumnName,
                Kind = DocColumnKind.Text,
                Width = 110f,
            };
            AddColumnWithDefaults(edgeTable, toPinColumn);
            updated = true;
        }

        DocColumn? edgeSubtableColumn = schema.EdgeSubtableColumn;
        if (edgeSubtableColumn == null)
        {
            edgeSubtableColumn = new DocColumn
            {
                Name = NodeGraphDefaultEdgesColumnName,
                Kind = DocColumnKind.Subtable,
                SubtableId = edgeTable.Id,
                Width = 120f,
            };
            AddColumnWithDefaults(table, edgeSubtableColumn);
            updated = true;
        }
        else if (!string.Equals(edgeSubtableColumn.SubtableId, edgeTable.Id, StringComparison.Ordinal))
        {
            edgeSubtableColumn.SubtableId = edgeTable.Id;
            updated = true;
        }

        settings.TypeColumnId = typeColumn?.Id ?? "";
        settings.PositionColumnId = positionColumn?.Id ?? "";
        settings.TitleColumnId = titleColumn?.Id ?? "";
        settings.ExecutionOutputColumnId = executionOutputColumn?.Id ?? "";
        settings.EdgeSubtableColumnId = edgeSubtableColumn?.Id ?? "";

        resolvedSchema = ResolveNodeGraphSchema(project, table, settings);
        return updated;
    }

    private static bool EnsureNodeGraphTypeLayoutsContainActiveSchemaColumns(
        DocTable table,
        NodeGraphResolvedSchema schema,
        NodeGraphViewSettingsPayload settings)
    {
        bool updated = false;
        settings.TypeLayouts ??= new List<NodeGraphTypeLayoutPayload>();

        var typeNames = BuildNodeGraphTypeNames(table, schema.TypeColumn);
        for (int typeNameIndex = 0; typeNameIndex < typeNames.Count; typeNameIndex++)
        {
            NodeGraphTypeLayoutPayload typeLayout = GetOrCreateNodeGraphTypeLayout(settings, typeNames[typeNameIndex]);
            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                DocColumn column = table.Columns[columnIndex];
                if (IsNodeGraphReservedColumn(column, schema))
                {
                    continue;
                }

                if (FindNodeGraphFieldIndex(typeLayout.Fields, column.Id) >= 0)
                {
                    continue;
                }

                typeLayout.Fields.Add(new NodeGraphFieldLayoutPayload
                {
                    ColumnId = column.Id,
                    Mode = "Setting",
                });
                updated = true;
            }
        }

        return updated;
    }

    private static List<string> BuildNodeGraphTypeNames(DocTable table, DocColumn? typeColumn)
    {
        var names = new List<string>(8);
        AddUniqueNodeGraphTypeName(names, NodeGraphDefaultTypeOption);

        if (typeColumn != null && typeColumn.Options != null)
        {
            for (int optionIndex = 0; optionIndex < typeColumn.Options.Count; optionIndex++)
            {
                AddUniqueNodeGraphTypeName(names, typeColumn.Options[optionIndex]);
            }
        }

        if (typeColumn != null)
        {
            for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                AddUniqueNodeGraphTypeName(names, table.Rows[rowIndex].GetCell(typeColumn).StringValue ?? "");
            }
        }

        return names;
    }

    private static void AddUniqueNodeGraphTypeName(List<string> names, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string normalized = value.Trim();
        for (int index = 0; index < names.Count; index++)
        {
            if (string.Equals(names[index], normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        names.Add(normalized);
    }

    private static NodeGraphTypeLayoutPayload GetOrCreateNodeGraphTypeLayout(
        NodeGraphViewSettingsPayload settings,
        string typeName)
    {
        for (int layoutIndex = 0; layoutIndex < settings.TypeLayouts.Count; layoutIndex++)
        {
            NodeGraphTypeLayoutPayload layout = settings.TypeLayouts[layoutIndex];
            if (string.Equals(layout.TypeName, typeName, StringComparison.OrdinalIgnoreCase))
            {
                layout.Fields ??= new List<NodeGraphFieldLayoutPayload>();
                layout.NodeWidth = ClampNodeGraphNodeWidth(layout.NodeWidth);
                return layout;
            }
        }

        var createdLayout = new NodeGraphTypeLayoutPayload
        {
            TypeName = typeName,
            NodeWidth = NodeGraphDefaultNodeWidth,
            Fields = new List<NodeGraphFieldLayoutPayload>(),
        };
        settings.TypeLayouts.Add(createdLayout);
        return createdLayout;
    }

    private static int FindNodeGraphFieldIndex(List<NodeGraphFieldLayoutPayload> fields, string columnId)
    {
        for (int fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            if (string.Equals(fields[fieldIndex].ColumnId, columnId, StringComparison.Ordinal))
            {
                return fieldIndex;
            }
        }

        return -1;
    }

    private static bool IsNodeGraphReservedColumn(DocColumn column, NodeGraphResolvedSchema schema)
    {
        return string.Equals(column.Id, schema.TypeColumn?.Id, StringComparison.Ordinal) ||
               string.Equals(column.Id, schema.PositionColumn?.Id, StringComparison.Ordinal) ||
               string.Equals(column.Id, schema.TitleColumn?.Id, StringComparison.Ordinal) ||
               string.Equals(column.Id, schema.ExecutionOutputColumn?.Id, StringComparison.Ordinal) ||
               string.Equals(column.Id, schema.EdgeSubtableColumn?.Id, StringComparison.Ordinal);
    }

    private static bool TryNormalizeNodeGraphFieldMode(string rawMode, out string normalizedMode)
    {
        normalizedMode = "Setting";
        if (string.IsNullOrWhiteSpace(rawMode))
        {
            return false;
        }

        if (string.Equals(rawMode, "Hidden", StringComparison.OrdinalIgnoreCase))
        {
            normalizedMode = "Hidden";
            return true;
        }

        if (string.Equals(rawMode, "Setting", StringComparison.OrdinalIgnoreCase))
        {
            normalizedMode = "Setting";
            return true;
        }

        if (string.Equals(rawMode, "InputPin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawMode, "Input pin", StringComparison.OrdinalIgnoreCase))
        {
            normalizedMode = "InputPin";
            return true;
        }

        if (string.Equals(rawMode, "OutputPin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawMode, "Output pin", StringComparison.OrdinalIgnoreCase))
        {
            normalizedMode = "OutputPin";
            return true;
        }

        return false;
    }

    private static float ClampNodeGraphNodeWidth(float width)
    {
        if (!float.IsFinite(width) || width <= 0f)
        {
            return NodeGraphDefaultNodeWidth;
        }

        return Math.Clamp(width, NodeGraphMinNodeWidth, NodeGraphMaxNodeWidth);
    }

    private static bool AreNodeGraphFieldsEqual(
        List<NodeGraphFieldLayoutPayload> left,
        List<NodeGraphFieldLayoutPayload> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            NodeGraphFieldLayoutPayload leftField = left[index];
            NodeGraphFieldLayoutPayload rightField = right[index];
            if (!string.Equals(leftField.ColumnId, rightField.ColumnId, StringComparison.Ordinal) ||
                !string.Equals(leftField.Mode, rightField.Mode, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static DocTable? FindNodeGraphEdgeTableCandidate(DocProject project, DocTable nodeTable)
    {
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable candidateTable = project.Tables[tableIndex];
            if (!string.Equals(candidateTable.ParentTableId, nodeTable.Id, StringComparison.Ordinal))
            {
                continue;
            }

            DocColumn? fromNodeColumn = FindColumnByNameAndKind(candidateTable, NodeGraphDefaultFromNodeColumnName, DocColumnKind.Relation);
            DocColumn? toNodeColumn = FindColumnByNameAndKind(candidateTable, NodeGraphDefaultToNodeColumnName, DocColumnKind.Relation);
            if (fromNodeColumn != null && toNodeColumn != null)
            {
                return candidateTable;
            }
        }

        return null;
    }

    private static DocColumn? FindColumnByNameAndKind(DocTable table, string columnName, DocColumnKind kind)
    {
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn column = table.Columns[columnIndex];
            if (column.Kind == kind &&
                string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase))
            {
                return column;
            }
        }

        return null;
    }

    private static void AddColumnWithDefaults(DocTable table, DocColumn column)
    {
        table.Columns.Add(column);
        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            table.Rows[rowIndex].SetCell(column.Id, DocCellValue.Default(column));
        }
    }

    private static bool ContainsOption(List<string> options, string optionValue)
    {
        for (int optionIndex = 0; optionIndex < options.Count; optionIndex++)
        {
            if (string.Equals(options[optionIndex], optionValue, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static object BuildNodeGraphSchemaPayload(NodeGraphResolvedSchema schema)
    {
        return new
        {
            typeColumnId = schema.TypeColumn?.Id ?? "",
            positionColumnId = schema.PositionColumn?.Id ?? "",
            titleColumnId = schema.TitleColumn?.Id ?? "",
            executionOutputColumnId = schema.ExecutionOutputColumn?.Id ?? "",
            edgeSubtableColumnId = schema.EdgeSubtableColumn?.Id ?? "",
            edgeTableId = schema.EdgeTable?.Id ?? "",
            edgeFromNodeColumnId = schema.EdgeFromNodeColumn?.Id ?? "",
            edgeFromPinColumnId = schema.EdgeFromPinColumn?.Id ?? "",
            edgeToNodeColumnId = schema.EdgeToNodeColumn?.Id ?? "",
            edgeToPinColumnId = schema.EdgeToPinColumn?.Id ?? "",
        };
    }

    private static object BuildNodeGraphSettingsPayload(
        DocTable table,
        NodeGraphResolvedSchema schema,
        NodeGraphViewSettingsPayload settings)
    {
        var typeLayouts = new List<object>(settings.TypeLayouts.Count);
        for (int layoutIndex = 0; layoutIndex < settings.TypeLayouts.Count; layoutIndex++)
        {
            NodeGraphTypeLayoutPayload typeLayout = settings.TypeLayouts[layoutIndex];
            var fields = new List<object>(typeLayout.Fields.Count);
            for (int fieldIndex = 0; fieldIndex < typeLayout.Fields.Count; fieldIndex++)
            {
                NodeGraphFieldLayoutPayload field = typeLayout.Fields[fieldIndex];
                DocColumn? column = FindColumn(table, field.ColumnId);
                fields.Add(new
                {
                    columnId = field.ColumnId,
                    mode = field.Mode,
                    columnName = column?.Name ?? "",
                    columnKind = column?.Kind.ToString() ?? "",
                    isReserved = column != null && IsNodeGraphReservedColumn(column, schema),
                });
            }

            typeLayouts.Add(new
            {
                typeName = typeLayout.TypeName,
                nodeWidth = ClampNodeGraphNodeWidth(typeLayout.NodeWidth),
                fields,
            });
        }

        return new
        {
            typeColumnId = settings.TypeColumnId ?? "",
            positionColumnId = settings.PositionColumnId ?? "",
            titleColumnId = settings.TitleColumnId ?? "",
            executionOutputColumnId = settings.ExecutionOutputColumnId ?? "",
            edgeSubtableColumnId = settings.EdgeSubtableColumnId ?? "",
            typeLayouts,
        };
    }

    private sealed class NodeGraphViewSettingsPayload
    {
        public string TypeColumnId { get; set; } = "";
        public string PositionColumnId { get; set; } = "";
        public string TitleColumnId { get; set; } = "";
        public string ExecutionOutputColumnId { get; set; } = "";
        public string EdgeSubtableColumnId { get; set; } = "";
        public List<NodeGraphTypeLayoutPayload> TypeLayouts { get; set; } = new();
    }

    private sealed class NodeGraphTypeLayoutPayload
    {
        public string TypeName { get; set; } = "";
        public float NodeWidth { get; set; } = NodeGraphDefaultNodeWidth;
        public List<NodeGraphFieldLayoutPayload> Fields { get; set; } = new();
    }

    private sealed class NodeGraphFieldLayoutPayload
    {
        public string ColumnId { get; set; } = "";
        public string Mode { get; set; } = "Setting";
    }

    private sealed class NodeGraphResolvedSchema
    {
        public DocColumn? TypeColumn { get; init; }
        public DocColumn? PositionColumn { get; init; }
        public DocColumn? TitleColumn { get; init; }
        public DocColumn? ExecutionOutputColumn { get; init; }
        public DocColumn? EdgeSubtableColumn { get; init; }
        public DocTable? EdgeTable { get; init; }
        public DocColumn? EdgeFromNodeColumn { get; init; }
        public DocColumn? EdgeFromPinColumn { get; init; }
        public DocColumn? EdgeToNodeColumn { get; init; }
        public DocColumn? EdgeToPinColumn { get; init; }
    }

    private static string GetStringOrEmpty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return "";
        }

        if (!element.TryGetProperty(propertyName, out var p) || p.ValueKind != JsonValueKind.String)
        {
            return "";
        }

        return p.GetString() ?? "";
    }

    private static string GetArgString(JsonElement args, string propertyName)
    {
        if (args.ValueKind != JsonValueKind.Object)
        {
            return "";
        }

        if (!args.TryGetProperty(propertyName, out var v) || v.ValueKind != JsonValueKind.String)
        {
            return "";
        }

        return v.GetString() ?? "";
    }

    private static int GetArgInt(JsonElement args, string propertyName, int fallback)
    {
        if (args.ValueKind != JsonValueKind.Object)
        {
            return fallback;
        }

        if (!args.TryGetProperty(propertyName, out var v) || v.ValueKind != JsonValueKind.Number)
        {
            return fallback;
        }

        return v.GetInt32();
    }

    private static bool GetArgBool(JsonElement args, string propertyName, bool fallback)
    {
        if (args.ValueKind != JsonValueKind.Object)
        {
            return fallback;
        }

        if (!args.TryGetProperty(propertyName, out var v) || (v.ValueKind != JsonValueKind.True && v.ValueKind != JsonValueKind.False))
        {
            return fallback;
        }

        return v.GetBoolean();
    }

    private static string WriteResult(JsonElement? idElement, Action<Utf8JsonWriter> writeResult)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WritePropertyName("id");
            if (idElement.HasValue)
            {
                idElement.Value.WriteTo(writer);
            }
            else
            {
                writer.WriteNullValue();
            }
            writer.WritePropertyName("result");
            writeResult(writer);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string WriteError(JsonElement? idElement, int code, string message, object? data)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            if (idElement.HasValue)
            {
                writer.WritePropertyName("id");
                idElement.Value.WriteTo(writer);
            }
            else
            {
                writer.WriteNull("id");
            }

            writer.WritePropertyName("error");
            writer.WriteStartObject();
            writer.WriteNumber("code", code);
            writer.WriteString("message", message);
            if (data != null)
            {
                writer.WritePropertyName("data");
                JsonSerializer.Serialize(writer, data);
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
