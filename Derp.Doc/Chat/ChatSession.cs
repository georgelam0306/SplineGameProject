namespace Derp.Doc.Chat;

internal sealed class ChatSession
{
    public readonly List<ChatMessage> Messages = new();
    public bool IsProcessing;
    public bool IsConnected;
    public string ErrorMessage = "";
    public bool RequestScrollToBottom;
    public ChatProviderKind ProviderKind = ChatProviderKind.Claude;
    public ChatAgentType AgentType = ChatAgentType.Mcp;
    public string CurrentModel = "";
    public readonly List<string> ModelOptions = new();
    public readonly List<string> ProviderCommands = new();
    public readonly List<string> AvailableTools = new();
    public readonly List<string> McpServers = new();
    public bool IsMcpOnly;
    public string LastUserMessage = "";

    public void ClearMessages()
    {
        Messages.Clear();
        RequestScrollToBottom = true;
        ErrorMessage = "";
        IsProcessing = false;
    }

    public void ApplyCapabilities(ChatProviderCapabilities capabilities)
    {
        CurrentModel = capabilities.CurrentModel;
        IsMcpOnly = capabilities.IsMcpOnly;

        ModelOptions.Clear();
        for (int modelIndex = 0; modelIndex < capabilities.ModelOptions.Count; modelIndex++)
        {
            ModelOptions.Add(capabilities.ModelOptions[modelIndex]);
        }

        ProviderCommands.Clear();
        for (int commandIndex = 0; commandIndex < capabilities.ProviderCommands.Count; commandIndex++)
        {
            ProviderCommands.Add(capabilities.ProviderCommands[commandIndex]);
        }

        AvailableTools.Clear();
        for (int toolIndex = 0; toolIndex < capabilities.AvailableTools.Count; toolIndex++)
        {
            AvailableTools.Add(capabilities.AvailableTools[toolIndex]);
        }

        McpServers.Clear();
        for (int serverIndex = 0; serverIndex < capabilities.McpServers.Count; serverIndex++)
        {
            McpServers.Add(capabilities.McpServers[serverIndex]);
        }
    }
}
