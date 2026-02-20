using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace Derp.Doc.Chat;

internal sealed class ClaudeChatProvider : IChatProvider
{
    private const string ClaudeBinaryName = "claude";
    private const string DerpDocMcpToolPattern = "mcp__derpdoc__*";

    private readonly ConcurrentQueue<ChatEvent> _events = new();
    private readonly object _gate = new();
    private readonly List<string> _models = new();
    private readonly List<string> _providerCommands = new();
    private readonly List<string> _availableTools = new();
    private readonly List<string> _mcpServers = new();

    private Process? _process;
    private StreamWriter? _stdin;
    private Thread? _stdoutThread;
    private Thread? _stderrThread;
    private bool _stopRequested;
    private string _workspaceRoot = "";
    private string _dbRoot = "";
    private string _model = "";
    private ChatAgentType _agentType = ChatAgentType.Mcp;

    public ChatProviderKind Kind => ChatProviderKind.Claude;
    public bool IsRunning => _process != null && !_process.HasExited;
    public bool IsMcpOnly => _agentType == ChatAgentType.Mcp;
    public ChatAgentType AgentType => _agentType;
    public string CurrentModel => _model;

    public void Start(string workspaceRoot, string dbRoot, ChatAgentType agentType)
    {
        lock (_gate)
        {
            if (IsRunning)
            {
                return;
            }

            _workspaceRoot = workspaceRoot;
            _dbRoot = dbRoot;
            _agentType = agentType;
            _stopRequested = false;
            InitializeDefaultCapabilities();

            if (!TryStartProcessLocked())
            {
                return;
            }
        }

        _events.Enqueue(new ChatEvent(ChatEventKind.Connected));
    }

    public void Stop()
    {
        Process? processToStop;
        StreamWriter? stdinToClose;

        lock (_gate)
        {
            _stopRequested = true;
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

        if (!IsRunning)
        {
            _events.Enqueue(new ChatEvent(ChatEventKind.Error, "Claude provider is not running.", isError: true));
            return;
        }

        string messageJson = JsonSerializer.Serialize(new
        {
            type = "user",
            message = new
            {
                role = "user",
                content = userMessage,
            },
        });

        try
        {
            _stdin?.WriteLine(messageJson);
            _stdin?.Flush();
        }
        catch (Exception ex)
        {
            _events.Enqueue(new ChatEvent(ChatEventKind.Error, "Failed to write to Claude stdin: " + ex.Message, isError: true));
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
            ProviderName = "Claude",
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

        if (IsRunning)
        {
            Stop();
            Start(_workspaceRoot, _dbRoot, _agentType);
        }

        return true;
    }

    public void Dispose()
    {
        Stop();
    }

    private bool TryStartProcessLocked()
    {
        var startInfo = new ProcessStartInfo(ClaudeBinaryName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _workspaceRoot,
        };

        startInfo.ArgumentList.Add("--print");
        startInfo.ArgumentList.Add("--verbose");
        startInfo.ArgumentList.Add("--input-format");
        startInfo.ArgumentList.Add("stream-json");
        startInfo.ArgumentList.Add("--output-format");
        startInfo.ArgumentList.Add("stream-json");
        startInfo.ArgumentList.Add("--include-partial-messages");
        startInfo.ArgumentList.Add("--permission-mode");
        startInfo.ArgumentList.Add("bypassPermissions");
        startInfo.ArgumentList.Add("--strict-mcp-config");
        startInfo.ArgumentList.Add("--mcp-config");
        startInfo.ArgumentList.Add(Path.Combine(_workspaceRoot, ".mcp.json"));
        if (IsMcpOnly)
        {
            startInfo.ArgumentList.Add("--allowed-tools");
            startInfo.ArgumentList.Add(DerpDocMcpToolPattern);
        }
        else
        {
            startInfo.ArgumentList.Add("--add-dir");
            startInfo.ArgumentList.Add(_workspaceRoot);
        }

        if (!string.IsNullOrWhiteSpace(_model))
        {
            startInfo.ArgumentList.Add("--model");
            startInfo.ArgumentList.Add(_model);
        }

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            _events.Enqueue(new ChatEvent(ChatEventKind.Error, "Claude CLI not found or failed to start: " + ex.Message, isError: true));
            return false;
        }

        if (process == null)
        {
            _events.Enqueue(new ChatEvent(ChatEventKind.Error, "Claude CLI failed to start.", isError: true));
            return false;
        }

        _process = process;
        _stdin = process.StandardInput;

        _stdoutThread = new Thread(ReadStdoutLoop)
        {
            IsBackground = true,
            Name = "ClaudeChatStdout",
        };
        _stdoutThread.Start();

        _stderrThread = new Thread(ReadStderrLoop)
        {
            IsBackground = true,
            Name = "ClaudeChatStderr",
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
                Process? process = _process;
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

                ParseStdoutJsonLine(line);
            }
        }
        catch (Exception ex)
        {
            if (!_stopRequested)
            {
                _events.Enqueue(new ChatEvent(ChatEventKind.Error, "Claude stdout read failed: " + ex.Message, isError: true));
            }
        }
        finally
        {
            if (!_stopRequested)
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
                Process? process = _process;
                if (process == null)
                {
                    return;
                }

                string? line = process.StandardError.ReadLine();
                if (line == null)
                {
                    return;
                }

                if (!_stopRequested && !string.IsNullOrWhiteSpace(line))
                {
                    _events.Enqueue(new ChatEvent(ChatEventKind.Error, line, isError: true));
                }
            }
        }
        catch
        {
        }
    }

    private void ParseStdoutJsonLine(string line)
    {
        JsonDocument? jsonDocument = null;
        try
        {
            jsonDocument = JsonDocument.Parse(line);
        }
        catch (Exception ex)
        {
            _events.Enqueue(new ChatEvent(ChatEventKind.Error, "Failed to parse Claude JSON: " + ex.Message, isError: true));
            return;
        }

        using (jsonDocument)
        {
            JsonElement root = jsonDocument.RootElement;
            string type = GetString(root, "type");

            if (string.Equals(type, "system", StringComparison.Ordinal))
            {
                ParseSystemInit(root);
                return;
            }

            if (string.Equals(type, "stream_event", StringComparison.Ordinal))
            {
                ParseStreamEvent(root);
                return;
            }

            if (string.Equals(type, "user", StringComparison.Ordinal))
            {
                ParseToolResultUserMessage(root);
                return;
            }

            if (string.Equals(type, "assistant", StringComparison.Ordinal))
            {
                _events.Enqueue(new ChatEvent(ChatEventKind.AssistantMessageDone));
                return;
            }

            if (string.Equals(type, "result", StringComparison.Ordinal))
            {
                bool isError = GetBool(root, "is_error");
                if (isError)
                {
                    string resultText = GetString(root, "result");
                    _events.Enqueue(new ChatEvent(ChatEventKind.Error, resultText, isError: true));
                    return;
                }

                _events.Enqueue(new ChatEvent(ChatEventKind.AssistantMessageDone));
            }
        }
    }

    private void ParseSystemInit(JsonElement root)
    {
        lock (_gate)
        {
            _providerCommands.Clear();
            _availableTools.Clear();
            _mcpServers.Clear();

            string model = GetString(root, "model");
            if (!string.IsNullOrWhiteSpace(model))
            {
                _model = model;
                AddUnique(_models, model);
            }

            AddUnique(_providerCommands, "/model");
            AddUnique(_providerCommands, "/help");

            if (root.TryGetProperty("slash_commands", out JsonElement slashCommands) &&
                slashCommands.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement command in slashCommands.EnumerateArray())
                {
                    if (command.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    string value = command.GetString() ?? "";
                    if (value.Length == 0)
                    {
                        continue;
                    }

                    AddUnique(_providerCommands, "/" + value);
                }
            }

            if (root.TryGetProperty("tools", out JsonElement tools) &&
                tools.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement tool in tools.EnumerateArray())
                {
                    if (tool.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    string value = tool.GetString() ?? "";
                    if (value.Length == 0)
                    {
                        continue;
                    }

                    AddUnique(_availableTools, value);
                }
            }

            if (root.TryGetProperty("mcp_servers", out JsonElement mcpServers) &&
                mcpServers.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement server in mcpServers.EnumerateArray())
                {
                    if (!server.TryGetProperty("name", out JsonElement nameElement) ||
                        nameElement.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    string name = nameElement.GetString() ?? "";
                    if (name.Length == 0)
                    {
                        continue;
                    }

                    AddUnique(_mcpServers, name);
                }
            }
        }
    }

    private void ParseStreamEvent(JsonElement root)
    {
        if (!root.TryGetProperty("event", out JsonElement eventElement) ||
            eventElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        string eventType = GetString(eventElement, "type");
        if (string.Equals(eventType, "content_block_start", StringComparison.Ordinal))
        {
            ParseContentBlockStart(eventElement);
            return;
        }

        if (string.Equals(eventType, "content_block_delta", StringComparison.Ordinal))
        {
            ParseContentBlockDelta(eventElement);
            return;
        }

        if (string.Equals(eventType, "message_stop", StringComparison.Ordinal))
        {
            _events.Enqueue(new ChatEvent(ChatEventKind.AssistantMessageDone));
        }
    }

    private void ParseContentBlockStart(JsonElement eventElement)
    {
        if (!eventElement.TryGetProperty("content_block", out JsonElement contentBlock) ||
            contentBlock.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        string contentType = GetString(contentBlock, "type");
        if (!string.Equals(contentType, "tool_use", StringComparison.Ordinal))
        {
            return;
        }

        string toolName = GetString(contentBlock, "name");
        string toolInput = "{}";
        if (contentBlock.TryGetProperty("input", out JsonElement inputElement))
        {
            toolInput = inputElement.GetRawText();
        }

        _events.Enqueue(new ChatEvent(ChatEventKind.ToolUse, toolName: toolName, toolInput: toolInput));
    }

    private void ParseContentBlockDelta(JsonElement eventElement)
    {
        if (!eventElement.TryGetProperty("delta", out JsonElement delta) ||
            delta.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        string deltaType = GetString(delta, "type");
        if (!string.Equals(deltaType, "text_delta", StringComparison.Ordinal))
        {
            return;
        }

        string text = GetString(delta, "text");
        if (text.Length == 0)
        {
            return;
        }

        _events.Enqueue(new ChatEvent(ChatEventKind.AssistantTextDelta, text));
    }

    private void ParseToolResultUserMessage(JsonElement root)
    {
        if (!root.TryGetProperty("message", out JsonElement message) ||
            message.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!message.TryGetProperty("content", out JsonElement contentArray) ||
            contentArray.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement content in contentArray.EnumerateArray())
        {
            if (content.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string contentType = GetString(content, "type");
            if (!string.Equals(contentType, "tool_result", StringComparison.Ordinal))
            {
                continue;
            }

            bool isError = GetBool(content, "is_error");
            string toolResult = ExtractToolResultText(content);
            _events.Enqueue(new ChatEvent(ChatEventKind.ToolResult, toolResult, isError: isError));
        }
    }

    private static string ExtractToolResultText(JsonElement toolResultContent)
    {
        if (!toolResultContent.TryGetProperty("content", out JsonElement contentElement))
        {
            return "";
        }

        if (contentElement.ValueKind == JsonValueKind.String)
        {
            return contentElement.GetString() ?? "";
        }

        return contentElement.GetRawText();
    }

    private void InitializeDefaultCapabilities()
    {
        lock (_gate)
        {
            _providerCommands.Clear();
            _availableTools.Clear();
            _mcpServers.Clear();
            AddUnique(_providerCommands, "/help");
            AddUnique(_providerCommands, "/model");
            AddUnique(_providerCommands, "/mcp");
            AddUnique(_mcpServers, "derpdoc");
            AddUnique(_availableTools, DerpDocMcpToolPattern);
        }
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

    private static bool GetBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement propertyElement))
        {
            return false;
        }

        return propertyElement.ValueKind == JsonValueKind.True;
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
