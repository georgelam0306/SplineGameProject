namespace Derp.UI;

internal readonly struct StateMachineGraphNodeRef
{
    public readonly StateMachineGraphNodeKind Kind;
    public readonly int Id;

    public StateMachineGraphNodeRef(StateMachineGraphNodeKind kind, int id)
    {
        Kind = kind;
        Id = id;
    }

    public int StateId => Id;

    public static StateMachineGraphNodeRef None() => new(StateMachineGraphNodeKind.None, 0);
    public static StateMachineGraphNodeRef Entry() => new(StateMachineGraphNodeKind.Entry, 0);
    public static StateMachineGraphNodeRef AnyState() => new(StateMachineGraphNodeKind.AnyState, 0);
    public static StateMachineGraphNodeRef Exit() => new(StateMachineGraphNodeKind.Exit, 0);
    public static StateMachineGraphNodeRef State(int id) => new(StateMachineGraphNodeKind.State, id);
}
