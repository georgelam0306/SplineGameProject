namespace Derp.Doc.Chat;

internal readonly struct ChatEvent
{
    public readonly ChatEventKind Kind;
    public readonly string Text;
    public readonly string ToolName;
    public readonly string ToolInput;
    public readonly bool IsError;

    public ChatEvent(ChatEventKind kind, string text = "", string toolName = "", string toolInput = "", bool isError = false)
    {
        Kind = kind;
        Text = text;
        ToolName = toolName;
        ToolInput = toolInput;
        IsError = isError;
    }
}
