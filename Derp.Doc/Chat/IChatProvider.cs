namespace Derp.Doc.Chat;

internal interface IChatProvider : IDisposable
{
    ChatProviderKind Kind { get; }
    bool IsRunning { get; }
    bool IsMcpOnly { get; }
    ChatAgentType AgentType { get; }
    string CurrentModel { get; }

    void Start(string workspaceRoot, string dbRoot, ChatAgentType agentType);
    void Stop();
    void SendMessage(string userMessage);
    bool TryDequeueEvent(out ChatEvent chatEvent);
    bool TryGetCapabilities(out ChatProviderCapabilities capabilities);
    bool TrySetModel(string modelName, out string errorMessage);
}
