namespace Derp.Doc.Chat;

internal static class ChatProviderFactory
{
    public static IChatProvider Create(ChatProviderKind providerKind)
    {
        if (providerKind == ChatProviderKind.Codex)
        {
            return new CodexChatProvider();
        }

        return new ClaudeChatProvider();
    }
}
