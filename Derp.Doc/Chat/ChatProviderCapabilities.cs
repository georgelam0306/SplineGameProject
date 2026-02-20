namespace Derp.Doc.Chat;

internal sealed class ChatProviderCapabilities
{
    public string ProviderName { get; set; } = "";
    public string CurrentModel { get; set; } = "";
    public List<string> ModelOptions { get; } = new();
    public List<string> ProviderCommands { get; } = new();
    public List<string> AvailableTools { get; } = new();
    public List<string> McpServers { get; } = new();
    public bool IsMcpOnly { get; set; }
}
