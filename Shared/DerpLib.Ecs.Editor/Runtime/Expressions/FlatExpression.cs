namespace DerpLib.Ecs.Editor;

/// <summary>
/// Flattened SSA representation of an expression, ready for evaluation.
/// </summary>
public readonly struct FlatExpression
{
    public readonly FlatOp[] Ops;
    public readonly int ResultIndex;

    public FlatExpression(FlatOp[] ops, int resultIndex)
    {
        Ops = ops;
        ResultIndex = resultIndex;
    }

    public bool IsValid => Ops != null && Ops.Length > 0;
}
