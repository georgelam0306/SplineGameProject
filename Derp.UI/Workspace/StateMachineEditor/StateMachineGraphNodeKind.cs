namespace Derp.UI;

internal enum StateMachineGraphNodeKind : byte
{
    State = 0,
    Entry = 1,
    AnyState = 2,
    Exit = 3,
    None = 255
}
