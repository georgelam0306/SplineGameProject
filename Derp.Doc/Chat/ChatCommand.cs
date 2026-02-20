namespace Derp.Doc.Chat;

internal enum ChatCommandKind
{
    None,
    Local,
    Provider,
}

internal readonly struct ChatCommand
{
    public readonly ChatCommandKind Kind;
    public readonly string Name;
    public readonly string Argument;

    public ChatCommand(ChatCommandKind kind, string name, string argument)
    {
        Kind = kind;
        Name = name;
        Argument = argument;
    }
}
