namespace DerpLib.Ecs.Editor;

public enum TokenKind : byte
{
    // Literals
    IntLiteral,
    FloatLiteral,
    BoolLiteral,
    StringLiteral,
    ColorLiteral,

    // Identifiers
    Variable,       // @name
    Identifier,     // function names

    // Arithmetic
    Plus,
    Minus,
    Star,
    Slash,

    // Comparison
    EqualEqual,
    BangEqual,
    Less,
    Greater,
    LessEqual,
    GreaterEqual,

    // Logical
    AmpAmp,
    PipePipe,
    Bang,

    // Ternary
    Question,
    Colon,

    // Grouping
    LeftParen,
    RightParen,
    Comma,

    // Special
    End,
    Error,
}

public readonly struct ExpressionToken
{
    public readonly TokenKind Kind;
    public readonly int Offset;
    public readonly int Length;

    // Pre-parsed literal values (avoids re-parsing later)
    public readonly float FloatValue;
    public readonly int IntValue;
    public readonly bool BoolValue;
    public readonly uint ColorValue; // ARGB packed

    public ExpressionToken(TokenKind kind, int offset, int length)
    {
        Kind = kind;
        Offset = offset;
        Length = length;
    }

    public ExpressionToken(TokenKind kind, int offset, int length, float floatValue) : this(kind, offset, length)
    {
        FloatValue = floatValue;
    }

    public ExpressionToken(TokenKind kind, int offset, int length, int intValue) : this(kind, offset, length)
    {
        IntValue = intValue;
    }

    public ExpressionToken(TokenKind kind, int offset, int length, bool boolValue) : this(kind, offset, length)
    {
        BoolValue = boolValue;
    }

    public ExpressionToken(TokenKind kind, int offset, int length, uint colorValue) : this(kind, offset, length)
    {
        ColorValue = colorValue;
    }
}
