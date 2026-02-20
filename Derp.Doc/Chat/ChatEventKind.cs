namespace Derp.Doc.Chat;

internal enum ChatEventKind
{
    Connected,
    Disconnected,
    AssistantTextDelta,
    AssistantMessageDone,
    ToolUse,
    ToolResult,
    Error,
}
