using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace Derp.Doc.Chat;

internal sealed class CodexChatProvider : IChatProvider
{
    private enum PendingRequestKind : byte
    {
        Initialize,
        ModelList,
        ThreadOpen,
        TurnStart,
    }

    private const string CodexBinaryName = "codex";
    private const string DerpDocMcpServerName = "derpdoc";
    private const string DerpDocMcpToolPattern = "mcp__derpdoc__*";

    private readonly ConcurrentQueue<ChatEvent> _events = new();
    private readonly object _gate = new();
    private readonly Queue<string> _queuedMessages = new();
    private readonly Dictionary<long, PendingRequestKind> _pendingRequests = new();
    private readonly HashSet<string> _assistantDeltaItemIds = new(StringComparer.Ordinal);
    private readonly List<string> _models = new();
    private readonly List<string> _providerCommands = new();
    private readonly List<string> _availableTools = new();
    private readonly List<string> _mcpServers = new();

    private string _workspaceRoot = "";
    private string _dbRoot = "";
    private string _model = "";
    private string _threadId = "";
    private bool _started;
    private bool _isProcessing;
    private bool _stopRequested;
    private bool _threadOpenRequested;
    private bool _connectedNotified;
    private long _nextRequestId = 1;
    private Process? _process;
    private StreamWriter? _stdin;
    private Thread? _stdoutThread;
    private Thread? _stderrThread;
    private ChatAgentType _agentType = ChatAgentType.Mcp;

    public ChatProviderKind Kind => ChatProviderKind.Codex;
    public bool IsMcpOnly => _agentType == ChatAgentType.Mcp;
    public ChatAgentType AgentType => _agentType;
    public string CurrentModel => _model;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return IsRunningLocked();
            }
        }
    }

    public void Start(string workspaceRoot, string dbRoot, ChatAgentType agentType)
    {
        lock (_gate)
        {
            if (IsRunningLocked())
            {
                return;
            }

            _workspaceRoot = workspaceRoot;
            _dbRoot = dbRoot;
            _agentType = agentType;
            _threadId = "";
            _stopRequested = false;
            _started = true;
            _isProcessing = false;
            _threadOpenRequested = false;
            _connectedNotified = false;
            _nextRequestId = 1;
            _queuedMessages.Clear();
            _pendingRequests.Clear();
            _assistantDeltaItemIds.Clear();
            InitializeDefaultCapabilitiesLocked();

            if (!TryStartAppServerLocked())
            {
                _started = false;
                return;
            }

            long requestId = NextRequestIdLocked();
            _pendingRequests[requestId] = PendingRequestKind.Initialize;
            _ = TryWriteRequestLocked(requestId, "initialize", new
            {
                clientInfo = new
                {
                    name = "derpdoc",
                    version = "0",
                },
                capabilities = new { },
            });
        }
    }

    public void Stop()
    {
        Process? processToStop;
        StreamWriter? stdinToClose;

        lock (_gate)
        {
            _stopRequested = true;
            _started = false;
            _isProcessing = false;
            _threadOpenRequested = false;
            _connectedNotified = false;
            _threadId = "";
            _queuedMessages.Clear();
            _pendingRequests.Clear();
            _assistantDeltaItemIds.Clear();

            processToStop = _process;
            stdinToClose = _stdin;
            _process = null;
            _stdin = null;
            _stdoutThread = null;
            _stderrThread = null;
        }

        try
        {
            stdinToClose?.Dispose();
        }
        catch
        {
        }

        if (processToStop != null)
        {
            try
            {
                if (!processToStop.HasExited)
                {
                    processToStop.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            try
            {
                processToStop.Dispose();
            }
            catch
            {
            }
        }

        _events.Enqueue(new ChatEvent(ChatEventKind.Disconnected));
    }

    public void SendMessage(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return;
        }

        lock (_gate)
        {
            if (!IsRunningLocked())
            {
                _events.Enqueue(new ChatEvent(ChatEventKind.Error, "Codex provider is not running.", isError: true));
                return;
            }

            _queuedMessages.Enqueue(userMessage);
            DispatchNextQueuedMessageLocked();
        }
    }

    public bool TryDequeueEvent(out ChatEvent chatEvent)
    {
        return _events.TryDequeue(out chatEvent);
    }

    public bool TryGetCapabilities(out ChatProviderCapabilities capabilities)
    {
        var snapshot = new ChatProviderCapabilities
        {
            ProviderName = "Codex",
            CurrentModel = _model,
            IsMcpOnly = IsMcpOnly,
        };

        lock (_gate)
        {
            CopyList(_models, snapshot.ModelOptions);
            CopyList(_providerCommands, snapshot.ProviderCommands);
            CopyList(_availableTools, snapshot.AvailableTools);
            CopyList(_mcpServers, snapshot.McpServers);
        }

        capabilities = snapshot;
        return true;
    }

    public bool TrySetModel(string modelName, out string errorMessage)
    {
        errorMessage = "";
        if (string.IsNullOrWhiteSpace(modelName))
        {
            errorMessage = "Model is required.";
            return false;
        }

        lock (_gate)
        {
            _model = modelName.Trim();
            AddUnique(_models, _model);
        }

        return true;
    }

    public void Dispose()
    {
        Stop();
    }

    private bool TryStartAppServerLocked()
    {
        var startInfo = new ProcessStartInfo(CodexBinaryName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _workspaceRoot,
        };

        startInfo.ArgumentList.Add("app-server");
        AddDerpDocMcpOverrides(startInfo);

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            _events.Enqueue(new ChatEvent(ChatEventKind.Error, "Failed to start Codex app-server: " + ex.Message, isError: true));
            return false;
        }

        if (process == null)
        {
            _events.Enqueue(new ChatEvent(ChatEventKind.Error, "Failed to create Codex app-server process.", isError: true));
            return false;
        }

        _process = process;
        _stdin = process.StandardInput;

        _stdoutThread = new Thread(ReadStdoutLoop)
        {
            IsBackground = true,
            Name = "CodexAppServerStdout",
        };
        _stdoutThread.Start();

        _stderrThread = new Thread(ReadStderrLoop)
        {
            IsBackground = true,
            Name = "CodexAppServerStderr",
        };
        _stderrThread.Start();
        return true;
    }

    private void ReadStdoutLoop()
    {
        try
        {
            while (true)
            {
                Process? process;
                lock (_gate)
                {
                    process = _process;
                }

                if (process == null)
                {
                    return;
                }

                string? line = process.StandardOutput.ReadLine();
                if (line == null)
                {
                    break;
                }

                if (line.Length == 0)
                {
                    continue;
                }

                ProcessStdoutJsonLine(line);
            }
        }
        catch (Exception ex)
        {
            if (!_stopRequested)
            {
                _events.Enqueue(new ChatEvent(ChatEventKind.Error, "Codex app-server stdout failed: " + ex.Message, isError: true));
            }
        }
        finally
        {
            bool notifyDisconnect;
            lock (_gate)
            {
                notifyDisconnect = !_stopRequested;
                _started = false;
                _isProcessing = false;
                _threadOpenRequested = false;
                _connectedNotified = false;
                _queuedMessages.Clear();
                _pendingRequests.Clear();
                _assistantDeltaItemIds.Clear();
                _process = null;
                _stdin = null;
            }

            if (notifyDisconnect)
            {
                _events.Enqueue(new ChatEvent(ChatEventKind.Disconnected));
            }
        }
    }

    private void ReadStderrLoop()
    {
        try
        {
            while (true)
            {
                Process? process;
                lock (_gate)
                {
                    process = _process;
                }

                if (process == null)
                {
                    return;
                }

                string? line = process.StandardError.ReadLine();
                if (line == null)
                {
                    return;
                }

                // App-server can emit informational lines on stderr; avoid surfacing these as hard errors.
                if (!_stopRequested && line.Contains("error", StringComparison.OrdinalIgnoreCase))
                {
                    _events.Enqueue(new ChatEvent(ChatEventKind.Error, line, isError: true));
                }
            }
        }
        catch
        {
        }
    }

    private void ProcessStdoutJsonLine(string line)
    {
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (Exception ex)
        {
            _events.Enqueue(new ChatEvent(ChatEventKind.Error, "Failed to parse Codex app-server JSON: " + ex.Message, isError: true));
            return;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            string method = GetString(root, "method");
            bool hasMethod = !string.IsNullOrWhiteSpace(method);
            bool hasId = root.TryGetProperty("id", out JsonElement idElement);

            if (hasMethod && hasId)
            {
                HandleServerRequest(root, idElement, method);
                return;
            }

            if (hasMethod)
            {
                HandleServerNotification(root, method);
                return;
            }

            if (hasId)
            {
                HandleServerResponse(root, idElement);
            }
        }
    }

    private void HandleServerResponse(JsonElement root, JsonElement idElement)
    {
        long requestId = ParseRequestId(idElement);
        PendingRequestKind pendingKind;
        bool hasPending;

        lock (_gate)
        {
            hasPending = _pendingRequests.TryGetValue(requestId, out pendingKind);
            if (hasPending)
            {
                _pendingRequests.Remove(requestId);
            }
        }

        if (root.TryGetProperty("error", out JsonElement errorElement) && errorElement.ValueKind == JsonValueKind.Object)
        {
            string errorMessage = GetString(errorElement, "message");
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                errorMessage = errorElement.GetRawText();
            }

            _events.Enqueue(new ChatEvent(ChatEventKind.Error, errorMessage, isError: true));
            if (hasPending && pendingKind == PendingRequestKind.TurnStart)
            {
                CompleteTurnAndDispatchNext();
            }
            return;
        }

        if (!hasPending)
        {
            return;
        }

        JsonElement resultElement = default;
        bool hasResult = root.TryGetProperty("result", out resultElement);

        switch (pendingKind)
        {
            case PendingRequestKind.Initialize:
                HandleInitializeResponse();
                break;
            case PendingRequestKind.ModelList:
                if (hasResult)
                {
                    HandleModelListResponse(resultElement);
                }
                break;
            case PendingRequestKind.ThreadOpen:
                if (hasResult)
                {
                    HandleThreadOpenResponse(resultElement);
                }
                break;
            case PendingRequestKind.TurnStart:
                // Completion is driven by turn/completed notifications.
                break;
        }
    }

    private void HandleInitializeResponse()
    {
        lock (_gate)
        {
            _ = TryWriteNotificationLocked("initialized", new { });

            long modelListRequestId = NextRequestIdLocked();
            _pendingRequests[modelListRequestId] = PendingRequestKind.ModelList;
            _ = TryWriteRequestLocked(modelListRequestId, "model/list", new { });

            RequestThreadOpenLocked();
        }
    }

    private void RequestThreadOpenLocked()
    {
        if (_threadOpenRequested)
        {
            return;
        }

        long requestId = NextRequestIdLocked();
        _pendingRequests[requestId] = PendingRequestKind.ThreadOpen;

        string? model = string.IsNullOrWhiteSpace(_model) ? null : _model;
        string developerInstructions = BuildPolicyInstructions();
        bool sent = _threadId.Length == 0
            ? TryWriteRequestLocked(requestId, "thread/start", new
            {
                cwd = _workspaceRoot,
                approvalPolicy = "never",
                sandbox = "workspace-write",
                developerInstructions,
                model,
            })
            : TryWriteRequestLocked(requestId, "thread/resume", new
            {
                threadId = _threadId,
                cwd = _workspaceRoot,
                approvalPolicy = "never",
                sandbox = "workspace-write",
                developerInstructions,
                model,
            });

        if (!sent)
        {
            _pendingRequests.Remove(requestId);
            return;
        }

        _threadOpenRequested = true;
    }

    private void HandleModelListResponse(JsonElement result)
    {
        if (!result.TryGetProperty("data", out JsonElement dataElement) ||
            dataElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        string defaultModel = "";
        for (int modelIndex = 0; modelIndex < dataElement.GetArrayLength(); modelIndex++)
        {
            JsonElement modelElement = dataElement[modelIndex];
            if (modelElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string modelName = GetString(modelElement, "model");
            if (string.IsNullOrWhiteSpace(modelName))
            {
                modelName = GetString(modelElement, "id");
            }

            if (string.IsNullOrWhiteSpace(modelName))
            {
                continue;
            }

            lock (_gate)
            {
                AddUnique(_models, modelName);
            }

            bool isDefault = modelElement.TryGetProperty("isDefault", out JsonElement defaultElement) &&
                defaultElement.ValueKind == JsonValueKind.True;
            if (isDefault && string.IsNullOrWhiteSpace(defaultModel))
            {
                defaultModel = modelName;
            }
        }

        if (string.IsNullOrWhiteSpace(defaultModel))
        {
            lock (_gate)
            {
                if (_models.Count > 0)
                {
                    defaultModel = _models[0];
                }
            }
        }

        if (string.IsNullOrWhiteSpace(defaultModel))
        {
            return;
        }

        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(_model))
            {
                _model = defaultModel;
            }
        }
    }

    private void HandleThreadOpenResponse(JsonElement result)
    {
        string resolvedThreadId = "";
        if (result.TryGetProperty("thread", out JsonElement threadElement) &&
            threadElement.ValueKind == JsonValueKind.Object)
        {
            resolvedThreadId = GetString(threadElement, "id");
        }

        if (string.IsNullOrWhiteSpace(resolvedThreadId))
        {
            _events.Enqueue(new ChatEvent(ChatEventKind.Error, "Codex thread/start returned no thread id.", isError: true));
            lock (_gate)
            {
                _threadOpenRequested = false;
            }
            return;
        }

        lock (_gate)
        {
            _threadId = resolvedThreadId;
            _threadOpenRequested = false;
            if (!_connectedNotified)
            {
                _events.Enqueue(new ChatEvent(ChatEventKind.Connected));
                _connectedNotified = true;
            }

            DispatchNextQueuedMessageLocked();
        }
    }

    private void HandleServerRequest(JsonElement root, JsonElement idElement, string method)
    {
        object? idValue = ParseJsonRpcIdValue(idElement);
        if (idValue == null)
        {
            return;
        }

        if (string.Equals(method, "item/commandExecution/requestApproval", StringComparison.Ordinal))
        {
            bool isMcpOnly = IsMcpOnly;
            string decision = isMcpOnly ? "decline" : "accept";
            if (isMcpOnly)
            {
                _events.Enqueue(new ChatEvent(ChatEventKind.Error, "Policy violation: shell command requested in MCP-only mode.", isError: true));
            }

            lock (_gate)
            {
                _ = TryWriteResultLocked(idValue, new { decision });
            }
            return;
        }

        if (string.Equals(method, "item/fileChange/requestApproval", StringComparison.Ordinal))
        {
            lock (_gate)
            {
                _ = TryWriteResultLocked(idValue, new { decision = "accept" });
            }
            return;
        }

        if (string.Equals(method, "item/tool/requestUserInput", StringComparison.Ordinal))
        {
            lock (_gate)
            {
                _ = TryWriteResultLocked(idValue, new { answers = new { } });
            }
            return;
        }

        if (string.Equals(method, "item/tool/call", StringComparison.Ordinal))
        {
            lock (_gate)
            {
                _ = TryWriteResultLocked(idValue, new
                {
                    success = false,
                    contentItems = new[]
                    {
                        new { type = "inputText", text = "Dynamic client tools are not supported by Derp.Doc." },
                    },
                });
            }
            return;
        }

        lock (_gate)
        {
            _ = TryWriteErrorLocked(idValue, -32601, "Unsupported server request method: " + method);
        }
    }

    private void HandleServerNotification(JsonElement root, string method)
    {
        JsonElement parameters = default;
        bool hasParams = root.TryGetProperty("params", out parameters) && parameters.ValueKind == JsonValueKind.Object;

        if (string.Equals(method, "thread/started", StringComparison.Ordinal))
        {
            if (hasParams && parameters.TryGetProperty("thread", out JsonElement threadElement) &&
                threadElement.ValueKind == JsonValueKind.Object)
            {
                string threadId = GetString(threadElement, "id");
                if (!string.IsNullOrWhiteSpace(threadId))
                {
                    lock (_gate)
                    {
                        _threadId = threadId;
                    }
                }
            }

            return;
        }

        if (string.Equals(method, "item/agentMessage/delta", StringComparison.Ordinal))
        {
            if (hasParams)
            {
                string delta = GetString(parameters, "delta");
                string itemId = GetString(parameters, "itemId");
                if (!string.IsNullOrWhiteSpace(itemId))
                {
                    _assistantDeltaItemIds.Add(itemId);
                }

                if (!string.IsNullOrWhiteSpace(delta))
                {
                    _events.Enqueue(new ChatEvent(ChatEventKind.AssistantTextDelta, delta));
                }
            }

            return;
        }

        if (string.Equals(method, "item/started", StringComparison.Ordinal))
        {
            if (hasParams && parameters.TryGetProperty("item", out JsonElement startedItem) &&
                startedItem.ValueKind == JsonValueKind.Object)
            {
                HandleStartedItem(startedItem);
            }

            return;
        }

        if (string.Equals(method, "item/completed", StringComparison.Ordinal))
        {
            if (hasParams && parameters.TryGetProperty("item", out JsonElement completedItem) &&
                completedItem.ValueKind == JsonValueKind.Object)
            {
                HandleCompletedItem(completedItem);
            }

            return;
        }

        if (string.Equals(method, "turn/completed", StringComparison.Ordinal))
        {
            HandleTurnCompleted(parameters);
            return;
        }

        if (string.Equals(method, "error", StringComparison.Ordinal))
        {
            string message = hasParams ? GetString(parameters, "message") : "";
            if (string.IsNullOrWhiteSpace(message) && hasParams)
            {
                message = parameters.GetRawText();
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                message = "Codex app-server reported an unspecified error.";
            }

            _events.Enqueue(new ChatEvent(ChatEventKind.Error, message, isError: true));
            CompleteTurnAndDispatchNext();
        }
    }

    private void HandleStartedItem(JsonElement item)
    {
        string itemType = GetString(item, "type");
        if (string.Equals(itemType, "mcpToolCall", StringComparison.Ordinal) ||
            string.Equals(itemType, "mcp_tool_call", StringComparison.Ordinal))
        {
            string server = GetString(item, "server");
            string tool = GetString(item, "tool");
            string toolName = string.IsNullOrWhiteSpace(server) ? tool : server + "." + tool;
            string toolInput = item.TryGetProperty("arguments", out JsonElement argsElement) ? argsElement.GetRawText() : "{}";
            _events.Enqueue(new ChatEvent(ChatEventKind.ToolUse, toolName: toolName, toolInput: toolInput));
            return;
        }

        if (string.Equals(itemType, "commandExecution", StringComparison.Ordinal) ||
            string.Equals(itemType, "command_execution", StringComparison.Ordinal))
        {
            string command = GetString(item, "command");
            if (IsMcpOnly)
            {
                string errorText = "Policy violation (shell command in MCP-only mode): " + command;
                _events.Enqueue(new ChatEvent(ChatEventKind.Error, errorText, isError: true));
                return;
            }

            _events.Enqueue(new ChatEvent(ChatEventKind.ToolUse, toolName: "shell", toolInput: command));
        }
    }

    private void HandleCompletedItem(JsonElement item)
    {
        string itemType = GetString(item, "type");
        if (string.Equals(itemType, "agentMessage", StringComparison.Ordinal) ||
            string.Equals(itemType, "agent_message", StringComparison.Ordinal))
        {
            string itemId = GetString(item, "id");
            if (!string.IsNullOrWhiteSpace(itemId) && _assistantDeltaItemIds.Contains(itemId))
            {
                return;
            }

            string text = GetString(item, "text");
            if (!string.IsNullOrWhiteSpace(text))
            {
                _events.Enqueue(new ChatEvent(ChatEventKind.AssistantTextDelta, text));
            }

            return;
        }

        if (string.Equals(itemType, "mcpToolCall", StringComparison.Ordinal) ||
            string.Equals(itemType, "mcp_tool_call", StringComparison.Ordinal))
        {
            string server = GetString(item, "server");
            string tool = GetString(item, "tool");
            string toolName = string.IsNullOrWhiteSpace(server) ? tool : server + "." + tool;
            string status = GetString(item, "status");
            if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                string errorMessage = "MCP tool call failed: " + toolName;
                if (item.TryGetProperty("error", out JsonElement errorElement) && errorElement.ValueKind == JsonValueKind.Object)
                {
                    string nestedMessage = GetString(errorElement, "message");
                    if (!string.IsNullOrWhiteSpace(nestedMessage))
                    {
                        errorMessage = nestedMessage;
                    }
                }

                _events.Enqueue(new ChatEvent(ChatEventKind.ToolResult, errorMessage, toolName: toolName, isError: true));
                return;
            }

            string resultText = item.TryGetProperty("result", out JsonElement resultElement)
                ? resultElement.GetRawText()
                : "{}";
            _events.Enqueue(new ChatEvent(ChatEventKind.ToolResult, resultText, toolName: toolName));
            return;
        }

        if (string.Equals(itemType, "commandExecution", StringComparison.Ordinal) ||
            string.Equals(itemType, "command_execution", StringComparison.Ordinal))
        {
            string command = GetString(item, "command");
            string status = GetString(item, "status");
            bool failed = string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(status, "declined", StringComparison.OrdinalIgnoreCase);
            string output = GetString(item, "aggregatedOutput");
            if (string.IsNullOrWhiteSpace(output))
            {
                output = GetString(item, "aggregated_output");
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                output = failed ? "Command failed." : "Command completed.";
            }

            _events.Enqueue(new ChatEvent(ChatEventKind.ToolResult, output, toolName: "shell", isError: failed));
            if (IsMcpOnly)
            {
                string errorText = "Policy violation (shell command in MCP-only mode): " + command;
                _events.Enqueue(new ChatEvent(ChatEventKind.Error, errorText, isError: true));
            }
        }
    }

    private void HandleTurnCompleted(JsonElement parameters)
    {
        string status = "";
        string errorMessage = "";
        if (parameters.ValueKind == JsonValueKind.Object &&
            parameters.TryGetProperty("turn", out JsonElement turnElement) &&
            turnElement.ValueKind == JsonValueKind.Object)
        {
            status = GetString(turnElement, "status");
            if (turnElement.TryGetProperty("error", out JsonElement errorElement) && errorElement.ValueKind == JsonValueKind.Object)
            {
                errorMessage = GetString(errorElement, "message");
            }
        }

        if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "interrupted", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                errorMessage = "Codex turn " + status + ".";
            }

            _events.Enqueue(new ChatEvent(ChatEventKind.Error, errorMessage, isError: true));
            CompleteTurnAndDispatchNext();
            return;
        }

        _events.Enqueue(new ChatEvent(ChatEventKind.AssistantMessageDone));
        CompleteTurnAndDispatchNext();
    }

    private void CompleteTurnAndDispatchNext()
    {
        lock (_gate)
        {
            _isProcessing = false;
            _assistantDeltaItemIds.Clear();
            DispatchNextQueuedMessageLocked();
        }
    }

    private void DispatchNextQueuedMessageLocked()
    {
        if (_isProcessing || _threadOpenRequested || string.IsNullOrWhiteSpace(_threadId) || _queuedMessages.Count == 0)
        {
            return;
        }

        string message = _queuedMessages.Dequeue();
        long requestId = NextRequestIdLocked();
        _pendingRequests[requestId] = PendingRequestKind.TurnStart;

        bool sent = TryWriteRequestLocked(requestId, "turn/start", new
        {
            threadId = _threadId,
            model = string.IsNullOrWhiteSpace(_model) ? null : _model,
            input = new[]
            {
                new
                {
                    type = "text",
                    text = message,
                },
            },
        });

        if (!sent)
        {
            _pendingRequests.Remove(requestId);
            _queuedMessages.Enqueue(message);
            return;
        }

        _isProcessing = true;
    }

    private long NextRequestIdLocked()
    {
        if (_nextRequestId == long.MaxValue)
        {
            _nextRequestId = 1;
        }

        long requestId = _nextRequestId;
        _nextRequestId++;
        return requestId;
    }

    private bool TryWriteRequestLocked(long id, string method, object? parameters)
    {
        object message = parameters == null
            ? new
            {
                jsonrpc = "2.0",
                id,
                method,
            }
            : new
            {
                jsonrpc = "2.0",
                id,
                method,
                @params = parameters,
            };

        return TryWriteJsonLineLocked(message);
    }

    private bool TryWriteNotificationLocked(string method, object? parameters)
    {
        object message = parameters == null
            ? new
            {
                jsonrpc = "2.0",
                method,
            }
            : new
            {
                jsonrpc = "2.0",
                method,
                @params = parameters,
            };

        return TryWriteJsonLineLocked(message);
    }

    private bool TryWriteResultLocked(object id, object result)
    {
        var message = new
        {
            jsonrpc = "2.0",
            id,
            result,
        };

        return TryWriteJsonLineLocked(message);
    }

    private bool TryWriteErrorLocked(object id, int code, string messageText)
    {
        var message = new
        {
            jsonrpc = "2.0",
            id,
            error = new
            {
                code,
                message = messageText,
            },
        };

        return TryWriteJsonLineLocked(message);
    }

    private bool TryWriteJsonLineLocked(object payload)
    {
        if (!IsRunningLocked() || _stdin == null)
        {
            _events.Enqueue(new ChatEvent(ChatEventKind.Error, "Codex app-server is not running.", isError: true));
            return false;
        }

        try
        {
            string line = JsonSerializer.Serialize(payload);
            _stdin.WriteLine(line);
            _stdin.Flush();
            return true;
        }
        catch (Exception ex)
        {
            _events.Enqueue(new ChatEvent(ChatEventKind.Error, "Failed writing Codex request: " + ex.Message, isError: true));
            return false;
        }
    }

    private bool IsRunningLocked()
    {
        return _started && _process != null && !_process.HasExited;
    }

    private static long ParseRequestId(JsonElement idElement)
    {
        if (idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt64(out long numericId))
        {
            return numericId;
        }

        if (idElement.ValueKind == JsonValueKind.String &&
            long.TryParse(idElement.GetString(), out long parsedId))
        {
            return parsedId;
        }

        return -1;
    }

    private static object? ParseJsonRpcIdValue(JsonElement idElement)
    {
        if (idElement.ValueKind == JsonValueKind.Number)
        {
            if (idElement.TryGetInt64(out long idAsLong))
            {
                return idAsLong;
            }

            if (idElement.TryGetDouble(out double idAsDouble))
            {
                return idAsDouble;
            }

            return idElement.GetRawText();
        }

        if (idElement.ValueKind == JsonValueKind.String)
        {
            return idElement.GetString() ?? "";
        }

        if (idElement.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return idElement.GetRawText();
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement propertyElement) ||
            propertyElement.ValueKind != JsonValueKind.String)
        {
            return "";
        }

        return propertyElement.GetString() ?? "";
    }

    private void InitializeDefaultCapabilitiesLocked()
    {
        _providerCommands.Clear();
        _availableTools.Clear();
        _mcpServers.Clear();

        AddUnique(_providerCommands, "/help");
        AddUnique(_providerCommands, "/model");
        AddUnique(_providerCommands, "/mcp");
        AddUnique(_mcpServers, DerpDocMcpServerName);
        AddUnique(_availableTools, DerpDocMcpToolPattern);
        if (!IsMcpOnly)
        {
            AddUnique(_availableTools, "workspace:*");
        }

        ParseModelFromCodexConfigLocked();
        ParseMcpServersFromCodexCliLocked();
    }

    private void ParseModelFromCodexConfigLocked()
    {
        try
        {
            string homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(homeDirectory))
            {
                return;
            }

            string configPath = Path.Combine(homeDirectory, ".codex", "config.toml");
            if (!File.Exists(configPath))
            {
                return;
            }

            string[] lines = File.ReadAllLines(configPath);
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex].Trim();
                if (!line.StartsWith("model", StringComparison.Ordinal))
                {
                    continue;
                }

                int equalsIndex = line.IndexOf('=');
                if (equalsIndex < 0 || equalsIndex >= line.Length - 1)
                {
                    continue;
                }

                string value = line[(equalsIndex + 1)..].Trim();
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                {
                    value = value[1..^1];
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                _model = value;
                AddUnique(_models, value);
                return;
            }
        }
        catch
        {
        }
    }

    private void ParseMcpServersFromCodexCliLocked()
    {
        string output = RunCommandCapture(CodexBinaryName, "mcp list", _workspaceRoot);
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex];
            if (line.StartsWith("Name", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int firstSpace = line.IndexOf(' ');
            string serverName = firstSpace > 0 ? line[..firstSpace] : line;
            if (string.IsNullOrWhiteSpace(serverName))
            {
                continue;
            }

            AddUnique(_mcpServers, serverName);
            AddUnique(_availableTools, "mcp__" + serverName + "__*");
        }
    }

    private static string RunCommandCapture(string fileName, string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return "";
            }

            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return stdout;
        }
        catch
        {
            return "";
        }
    }

    private void AddDerpDocMcpOverrides(ProcessStartInfo startInfo)
    {
        string escapedProjectPath = EscapeTomlString(Path.Combine(_workspaceRoot, "Tools", "DerpDoc.Cli"));
        string escapedWorkspacePath = EscapeTomlString(_workspaceRoot);

        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("mcp_servers.derpdoc.command=\"dotnet\"");

        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(
            "mcp_servers.derpdoc.args=[\"run\",\"--project\",\"" +
            escapedProjectPath +
            "\",\"--\",\"mcp\",\"--workspace\",\"" +
            escapedWorkspacePath +
            "\"]");
    }

    private string BuildPolicyInstructions()
    {
        if (IsMcpOnly)
        {
            return "MCP-only policy: Use DerpDoc MCP tools only. Do not run shell commands, do not edit files directly, and do not use web fetch/search tools. " +
                   "Always resolve active project before table/document mutations. Prefer batch tools for multi-row/multi-cell operations.";
        }

        return "Workspace-agent policy: You may use DerpDoc MCP tools plus workspace file/shell capabilities within the workspace root. " +
               "Prefer DerpDoc MCP tools for table/document data mutations when possible.";
    }

    private static string EscapeTomlString(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static void AddUnique(List<string> list, string value)
    {
        for (int index = 0; index < list.Count; index++)
        {
            if (string.Equals(list[index], value, StringComparison.Ordinal))
            {
                return;
            }
        }

        list.Add(value);
    }

    private static void CopyList(List<string> source, List<string> destination)
    {
        for (int index = 0; index < source.Count; index++)
        {
            destination.Add(source[index]);
        }
    }
}
