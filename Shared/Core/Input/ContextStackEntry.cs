namespace Core.Input;

/// <summary>
/// An entry on the input context stack.
/// </summary>
public readonly record struct ContextStackEntry(StringHandle Name, ContextBlockPolicy Policy);
