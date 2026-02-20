using Property;

namespace DerpLib.Ecs.Editor;

public enum FlatOpKind : byte
{
    // Load
    LoadLiteral,
    LoadSource,

    // Arithmetic
    Add,
    Subtract,
    Multiply,
    Divide,
    MulScalar,           // Vec * Float
    DivScalar,           // Vec / Float
    MulFixed64Scalar,    // Fixed64Vec * Fixed64
    DivFixed64Scalar,    // Fixed64Vec / Fixed64
    Negate,
    PromoteIntToFloat,
    PromoteIntToFixed64,

    // Comparison
    Equal,
    NotEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,

    // Logical
    And,
    Or,
    Not,

    // Ternary select
    Select,

    // Functions
    Min,
    Max,
    Clamp,
    Abs,
    Floor,
    Ceil,
    Round,
    Lerp,
    Remap,
}

/// <summary>
/// A single operation in the flattened SSA representation.
/// Indices refer to temp slots (positions in the FlatOp[] itself).
/// </summary>
public struct FlatOp
{
    public FlatOpKind Kind;
    public PropertyKind ResultType;

    /// <summary>For comparison ops: the type to dispatch on (operand type, not result type).</summary>
    public PropertyKind OperandType;

    /// <summary>Temp index for left operand / first arg (or -1).</summary>
    public int Left;
    /// <summary>Temp index for right operand / second arg (or -1).</summary>
    public int Right;
    /// <summary>Temp index for third arg (or -1).</summary>
    public int Arg2;
    /// <summary>Temp index for fourth arg (or -1).</summary>
    public int Arg3;
    /// <summary>Temp index for fifth arg (or -1).</summary>
    public int Arg4;

    /// <summary>Literal value for <see cref="FlatOpKind.LoadLiteral"/>.</summary>
    public PropertyValue LiteralValue;

    /// <summary>Source variable index for <see cref="FlatOpKind.LoadSource"/>.</summary>
    public int SourceIndex;
}
