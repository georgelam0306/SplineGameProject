using Property;

namespace DerpLib.Ecs.Editor;

public enum ExpressionKind : byte
{
    // Literals
    FloatLiteral,
    IntLiteral,
    BoolLiteral,
    StringLiteral,
    ColorLiteral,

    // Variable reference
    Variable,

    // Binary arithmetic
    Add,
    Subtract,
    Multiply,
    Divide,

    // Comparison
    Equal,
    NotEqual,
    Less,
    Greater,
    LessEqual,
    GreaterEqual,

    // Logical
    And,
    Or,
    Not,

    // Unary
    Negate,

    // Ternary
    Ternary,

    // Builtin function call
    FunctionCall,
}

/// <summary>
/// AST node for expressions. Nodes are stored in a flat array with children at lower indices
/// than their parents, enabling single-pass bottom-up type checking.
/// </summary>
public struct ExpressionNode
{
    public ExpressionKind Kind;

    /// <summary>Annotated by type checker. <see cref="PropertyKind.Auto"/> until resolved.</summary>
    public PropertyKind ResultType;

    // Literal values (valid when Kind is a literal)
    public PropertyValue LiteralValue;
    public PropertyKind LiteralKind;

    /// <summary>Index into the source variables array (for <see cref="ExpressionKind.Variable"/>).</summary>
    public int SourceIndex;

    // Child indices (-1 = unused). References into the same ExpressionNode[].
    public int LeftIndex;
    public int RightIndex;

    // Ternary
    public int ConditionIndex;
    public int TrueIndex;
    public int FalseIndex;

    // Function call
    public BuiltinFunction Function;
    public int Arg0;
    public int Arg1;
    public int Arg2;
    public int Arg3;
    public int Arg4;
    public int ArgCount;

    // Source location for error reporting
    public int SourceOffset;
    public int SourceLength;

    public static ExpressionNode Default => new()
    {
        LeftIndex = -1,
        RightIndex = -1,
        ConditionIndex = -1,
        TrueIndex = -1,
        FalseIndex = -1,
        Arg0 = -1,
        Arg1 = -1,
        Arg2 = -1,
        Arg3 = -1,
        Arg4 = -1,
        ResultType = PropertyKind.Auto,
    };
}
