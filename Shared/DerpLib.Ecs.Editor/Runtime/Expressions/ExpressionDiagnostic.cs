namespace DerpLib.Ecs.Editor;

public readonly struct ExpressionDiagnostic
{
    public readonly int Offset;
    public readonly int Length;
    public readonly string Message;

    public ExpressionDiagnostic(int offset, int length, string message)
    {
        Offset = offset;
        Length = length;
        Message = message;
    }

    public override string ToString() => $"[{Offset}..{Offset + Length}] {Message}";
}
