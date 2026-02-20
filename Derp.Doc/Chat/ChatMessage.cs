namespace Derp.Doc.Chat;

internal sealed class ChatMessage
{
    public ChatRole Role;
    public string Content = "";
    public string ToolName = "";
    public string ToolInput = "";
    public bool IsStreaming;
    public bool IsError;
    public bool IsExpanded;
}
