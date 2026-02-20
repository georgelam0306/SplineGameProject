namespace Derp.Doc.Chat;

internal static class ChatSlashCommandParser
{
    public static readonly string[] LocalCommands =
    [
        "/help",
        "/provider",
        "/agent",
        "/model",
        "/mcp",
        "/clear",
        "/retry",
    ];

    public static ChatCommand Parse(string rawText, IReadOnlyList<string> providerCommands)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return default;
        }

        if (rawText[0] != '/')
        {
            return default;
        }

        var span = rawText.AsSpan().Trim();
        int splitIndex = span.IndexOf(' ');

        ReadOnlySpan<char> nameSpan;
        ReadOnlySpan<char> argumentSpan;

        if (splitIndex < 0)
        {
            nameSpan = span;
            argumentSpan = ReadOnlySpan<char>.Empty;
        }
        else
        {
            nameSpan = span[..splitIndex];
            argumentSpan = span[(splitIndex + 1)..].Trim();
        }

        string commandName = nameSpan.ToString();
        string commandArgument = argumentSpan.ToString();

        for (int commandIndex = 0; commandIndex < LocalCommands.Length; commandIndex++)
        {
            if (string.Equals(commandName, LocalCommands[commandIndex], StringComparison.OrdinalIgnoreCase))
            {
                return new ChatCommand(ChatCommandKind.Local, commandName, commandArgument);
            }
        }

        for (int commandIndex = 0; commandIndex < providerCommands.Count; commandIndex++)
        {
            if (string.Equals(commandName, providerCommands[commandIndex], StringComparison.OrdinalIgnoreCase))
            {
                return new ChatCommand(ChatCommandKind.Provider, commandName, commandArgument);
            }
        }

        return new ChatCommand(ChatCommandKind.None, commandName, commandArgument);
    }

    public static int BuildSuggestions(string inputText, IReadOnlyList<string> providerCommands, Span<string> destination)
    {
        if (string.IsNullOrWhiteSpace(inputText) || inputText[0] != '/')
        {
            return 0;
        }

        string prefix = inputText.Trim();
        int count = 0;

        for (int commandIndex = 0; commandIndex < LocalCommands.Length; commandIndex++)
        {
            string command = LocalCommands[commandIndex];
            if (!command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (count >= destination.Length)
            {
                return count;
            }

            destination[count] = command;
            count++;
        }

        for (int commandIndex = 0; commandIndex < providerCommands.Count; commandIndex++)
        {
            string providerCommand = providerCommands[commandIndex];
            if (string.IsNullOrWhiteSpace(providerCommand))
            {
                continue;
            }

            string prefixed = providerCommand[0] == '/' ? providerCommand : "/" + providerCommand;
            if (!prefixed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool duplicate = false;
            for (int existingIndex = 0; existingIndex < count; existingIndex++)
            {
                if (string.Equals(destination[existingIndex], prefixed, StringComparison.OrdinalIgnoreCase))
                {
                    duplicate = true;
                    break;
                }
            }

            if (duplicate)
            {
                continue;
            }

            if (count >= destination.Length)
            {
                return count;
            }

            destination[count] = prefixed;
            count++;
        }

        return count;
    }
}
