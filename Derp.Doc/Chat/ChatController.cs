using Derp.Doc.Editor;

namespace Derp.Doc.Chat;

internal sealed class ChatController : IDisposable
{
    private IChatProvider? _provider;
    private ChatProviderKind _providerKind = ChatProviderKind.Claude;
    private ChatAgentType _agentType = ChatAgentType.Mcp;

    public ChatProviderKind ProviderKind => _providerKind;
    public ChatAgentType AgentType => _agentType;
    public bool IsProviderRunning => _provider != null && _provider.IsRunning;

    public void EnsureStarted(ChatProviderKind providerKind, ChatAgentType agentType, string workspaceRoot, string dbRoot)
    {
        if (_provider != null && _providerKind == providerKind && _agentType == agentType && _provider.IsRunning)
        {
            return;
        }

        _provider?.Dispose();
        _provider = ChatProviderFactory.Create(providerKind);
        _providerKind = providerKind;
        _agentType = agentType;
        _provider.Start(workspaceRoot, dbRoot, agentType);
    }

    public void Stop()
    {
        _provider?.Dispose();
        _provider = null;
    }

    public bool SendMessage(DocWorkspace workspace, string userMessage)
    {
        if (workspace.IsDirty)
        {
            workspace.ChatSession.ErrorMessage = "Chat disabled: unsaved local changes. Save or wait for autosave.";
            return false;
        }

        if (workspace.EditState.IsEditing)
        {
            workspace.CommitTableCellEditIfActive();
            if (workspace.EditState.IsEditing)
            {
                workspace.ChatSession.ErrorMessage = "Could not finish the current cell edit before sending chat.";
                return false;
            }
        }

        string dbRoot = workspace.ProjectPath ?? workspace.WorkspaceRoot;
        if (string.IsNullOrWhiteSpace(dbRoot))
        {
            dbRoot = workspace.WorkspaceRoot;
        }

        if (_provider == null)
        {
            EnsureStarted(workspace.ChatSession.ProviderKind, workspace.ChatSession.AgentType, workspace.WorkspaceRoot, dbRoot);
        }

        if (_provider == null || !_provider.IsRunning)
        {
            workspace.ChatSession.ErrorMessage = "Chat provider is not available.";
            return false;
        }

        string context = ChatContextBuilder.BuildSystemContext(workspace);
        string payload = context + "\n\nUser request:\n" + userMessage;

        workspace.ChatSession.Messages.Add(new ChatMessage
        {
            Role = ChatRole.User,
            Content = userMessage,
            IsStreaming = false,
        });

        workspace.ChatSession.LastUserMessage = userMessage;
        workspace.ChatSession.RequestScrollToBottom = true;
        workspace.ChatSession.IsProcessing = true;
        workspace.ChatSession.ErrorMessage = "";

        _provider.SendMessage(payload);
        return true;
    }

    public void Poll(DocWorkspace workspace)
    {
        if (_provider == null)
        {
            workspace.ChatSession.IsConnected = false;
            return;
        }

        if (_provider.TryGetCapabilities(out var capabilities))
        {
            workspace.ChatSession.ApplyCapabilities(capabilities);
        }

        while (_provider.TryDequeueEvent(out var chatEvent))
        {
            ApplyEvent(workspace.ChatSession, chatEvent);
        }

        workspace.ChatSession.IsConnected = _provider.IsRunning;
    }

    public bool TrySetModel(DocWorkspace workspace, string modelName, out string errorMessage)
    {
        errorMessage = "";
        if (_provider == null)
        {
            errorMessage = "Chat provider is not started.";
            return false;
        }

        bool set = _provider.TrySetModel(modelName, out errorMessage);
        if (!set)
        {
            return false;
        }

        workspace.ChatSession.Messages.Add(new ChatMessage
        {
            Role = ChatRole.System,
            Content = "Model set to: " + modelName,
        });
        workspace.ChatSession.RequestScrollToBottom = true;
        return true;
    }

    public void SwitchProvider(DocWorkspace workspace, ChatProviderKind providerKind)
    {
        if (_providerKind == providerKind && _provider != null && _provider.IsRunning)
        {
            return;
        }

        string dbRoot = workspace.ProjectPath ?? workspace.WorkspaceRoot;
        Stop();
        EnsureStarted(providerKind, workspace.ChatSession.AgentType, workspace.WorkspaceRoot, dbRoot);

        workspace.ChatSession.ProviderKind = providerKind;
        workspace.ChatSession.Messages.Add(new ChatMessage
        {
            Role = ChatRole.System,
            Content = "Switched provider to " + providerKind + ".",
        });
        workspace.ChatSession.RequestScrollToBottom = true;
    }

    public void SwitchAgentType(DocWorkspace workspace, ChatAgentType agentType)
    {
        if (_agentType == agentType && _provider != null && _provider.IsRunning)
        {
            return;
        }

        string dbRoot = workspace.ProjectPath ?? workspace.WorkspaceRoot;
        Stop();
        EnsureStarted(workspace.ChatSession.ProviderKind, agentType, workspace.WorkspaceRoot, dbRoot);

        workspace.ChatSession.AgentType = agentType;
        workspace.ChatSession.IsMcpOnly = agentType == ChatAgentType.Mcp;
        workspace.ChatSession.Messages.Add(new ChatMessage
        {
            Role = ChatRole.System,
            Content = "Switched agent type to " + DescribeAgentType(agentType) + ".",
        });
        workspace.ChatSession.RequestScrollToBottom = true;
    }

    public void Dispose()
    {
        Stop();
    }

    private static void ApplyEvent(ChatSession session, ChatEvent chatEvent)
    {
        switch (chatEvent.Kind)
        {
            case ChatEventKind.Connected:
                session.IsConnected = true;
                break;
            case ChatEventKind.Disconnected:
                session.IsConnected = false;
                session.IsProcessing = false;
                break;
            case ChatEventKind.AssistantTextDelta:
                AppendAssistantDelta(session, chatEvent.Text);
                break;
            case ChatEventKind.AssistantMessageDone:
                MarkAssistantDone(session);
                session.IsProcessing = false;
                break;
            case ChatEventKind.ToolUse:
                session.Messages.Add(new ChatMessage
                {
                    Role = ChatRole.ToolUse,
                    ToolName = chatEvent.ToolName,
                    ToolInput = chatEvent.ToolInput,
                    Content = chatEvent.ToolInput,
                    IsExpanded = false,
                });
                session.RequestScrollToBottom = true;
                break;
            case ChatEventKind.ToolResult:
                session.Messages.Add(new ChatMessage
                {
                    Role = ChatRole.ToolResult,
                    ToolName = chatEvent.ToolName,
                    Content = chatEvent.Text,
                    IsError = chatEvent.IsError,
                    IsExpanded = false,
                });
                session.RequestScrollToBottom = true;
                break;
            case ChatEventKind.Error:
                session.ErrorMessage = chatEvent.Text;
                session.IsProcessing = false;
                session.Messages.Add(new ChatMessage
                {
                    Role = ChatRole.Error,
                    Content = chatEvent.Text,
                    IsError = true,
                });
                session.RequestScrollToBottom = true;
                break;
        }
    }

    private static void AppendAssistantDelta(ChatSession session, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (session.Messages.Count > 0)
        {
            var last = session.Messages[session.Messages.Count - 1];
            if (last.Role == ChatRole.Assistant && last.IsStreaming)
            {
                last.Content += text;
                session.RequestScrollToBottom = true;
                return;
            }
        }

        session.Messages.Add(new ChatMessage
        {
            Role = ChatRole.Assistant,
            Content = text,
            IsStreaming = true,
        });
        session.RequestScrollToBottom = true;
    }

    private static void MarkAssistantDone(ChatSession session)
    {
        if (session.Messages.Count == 0)
        {
            return;
        }

        var last = session.Messages[session.Messages.Count - 1];
        if (last.Role == ChatRole.Assistant)
        {
            last.IsStreaming = false;
        }
    }

    private static string DescribeAgentType(ChatAgentType agentType)
    {
        return agentType == ChatAgentType.Workspace ? "Workspace Agent" : "MCP Agent";
    }
}
