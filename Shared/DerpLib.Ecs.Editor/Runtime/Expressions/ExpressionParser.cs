using System;
using System.Collections.Generic;
using Core;
using Property;

namespace DerpLib.Ecs.Editor;

/// <summary>
/// Recursive descent parser for the expression DSL.
/// Produces <see cref="ExpressionNode"/>[] in bottom-up order (children before parents).
/// Variable names are resolved to source indices via a caller-provided lookup.
/// </summary>
public sealed class ExpressionParser
{
    private ExpressionLexer _lexer;
    private ExpressionToken _current;
    private readonly ReadOnlyMemory<char> _source;
    private readonly List<ExpressionNode> _nodes;
    private readonly List<ExpressionDiagnostic> _diagnostics;
    private readonly Dictionary<string, int> _variableMap; // alias → source index

    public ExpressionParser(ReadOnlyMemory<char> source, Dictionary<string, int>? variableMap = null)
    {
        _source = source;
        _lexer = new ExpressionLexer(source);
        _nodes = new List<ExpressionNode>(32);
        _diagnostics = new List<ExpressionDiagnostic>();
        _variableMap = variableMap ?? new Dictionary<string, int>();
        _current = _lexer.NextToken();
    }

    public ExpressionParseResult Parse()
    {
        int rootIndex = ParseTernary();
        if (_current.Kind != TokenKind.End)
            Error(_current, "Unexpected token after expression");

        return new ExpressionParseResult(
            _nodes.ToArray(),
            rootIndex,
            _diagnostics.Count > 0 ? _diagnostics.ToArray() : Array.Empty<ExpressionDiagnostic>());
    }

    private int Emit(ExpressionNode node)
    {
        int index = _nodes.Count;
        _nodes.Add(node);
        return index;
    }

    private void Advance()
    {
        _current = _lexer.NextToken();
    }

    private bool Match(TokenKind kind)
    {
        if (_current.Kind == kind)
        {
            Advance();
            return true;
        }
        return false;
    }

    private void Expect(TokenKind kind, string errorMessage)
    {
        if (_current.Kind == kind)
        {
            Advance();
            return;
        }
        Error(_current, errorMessage);
    }

    private void Error(ExpressionToken token, string message)
    {
        _diagnostics.Add(new ExpressionDiagnostic(token.Offset, Math.Max(token.Length, 1), message));
    }

    private ReadOnlySpan<char> TokenText(ExpressionToken token)
    {
        return _source.Span.Slice(token.Offset, token.Length);
    }

    /// <summary>
    /// Extracts the unescaped string content from a StringLiteral token (strips quotes, handles \n \t \\ \").
    /// </summary>
    private string ExtractStringContent(ExpressionToken token)
    {
        // Strip surrounding quotes: "content"
        var inner = _source.Span.Slice(token.Offset + 1, token.Length - 2);

        // Fast path: no escape sequences
        if (inner.IndexOf('\\') < 0)
            return inner.ToString();

        // Slow path: unescape
        var sb = new System.Text.StringBuilder(inner.Length);
        for (int i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '\\' && i + 1 < inner.Length)
            {
                i++;
                sb.Append(inner[i] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '"' => '"',
                    '\\' => '\\',
                    _ => inner[i],
                });
            }
            else
            {
                sb.Append(inner[i]);
            }
        }
        return sb.ToString();
    }

    // ──────────────── Precedence Climbing ────────────────

    // 1. Ternary: ? :
    private int ParseTernary()
    {
        int left = ParseOr();

        if (_current.Kind == TokenKind.Question)
        {
            var qToken = _current;
            Advance();
            int trueExpr = ParseTernary(); // right-associative
            Expect(TokenKind.Colon, "Expected ':' in ternary expression");
            int falseExpr = ParseTernary();

            var node = ExpressionNode.Default;
            node.Kind = ExpressionKind.Ternary;
            node.ConditionIndex = left;
            node.TrueIndex = trueExpr;
            node.FalseIndex = falseExpr;
            node.SourceOffset = qToken.Offset;
            node.SourceLength = qToken.Length;
            return Emit(node);
        }
        return left;
    }

    // 2. Logical OR: ||
    private int ParseOr()
    {
        int left = ParseAnd();
        while (_current.Kind == TokenKind.PipePipe)
        {
            var opToken = _current;
            Advance();
            int right = ParseAnd();
            left = EmitBinary(ExpressionKind.Or, left, right, opToken);
        }
        return left;
    }

    // 3. Logical AND: &&
    private int ParseAnd()
    {
        int left = ParseEquality();
        while (_current.Kind == TokenKind.AmpAmp)
        {
            var opToken = _current;
            Advance();
            int right = ParseEquality();
            left = EmitBinary(ExpressionKind.And, left, right, opToken);
        }
        return left;
    }

    // 4. Equality: == !=
    private int ParseEquality()
    {
        int left = ParseComparison();
        while (_current.Kind is TokenKind.EqualEqual or TokenKind.BangEqual)
        {
            var opToken = _current;
            var kind = opToken.Kind == TokenKind.EqualEqual ? ExpressionKind.Equal : ExpressionKind.NotEqual;
            Advance();
            int right = ParseComparison();
            left = EmitBinary(kind, left, right, opToken);
        }
        return left;
    }

    // 5. Comparison: < > <= >=
    private int ParseComparison()
    {
        int left = ParseAdditive();
        while (_current.Kind is TokenKind.Less or TokenKind.Greater or TokenKind.LessEqual or TokenKind.GreaterEqual)
        {
            var opToken = _current;
            var kind = opToken.Kind switch
            {
                TokenKind.Less => ExpressionKind.Less,
                TokenKind.Greater => ExpressionKind.Greater,
                TokenKind.LessEqual => ExpressionKind.LessEqual,
                TokenKind.GreaterEqual => ExpressionKind.GreaterEqual,
                _ => ExpressionKind.Less,
            };
            Advance();
            int right = ParseAdditive();
            left = EmitBinary(kind, left, right, opToken);
        }
        return left;
    }

    // 6. Additive: + -
    private int ParseAdditive()
    {
        int left = ParseMultiplicative();
        while (_current.Kind is TokenKind.Plus or TokenKind.Minus)
        {
            var opToken = _current;
            var kind = opToken.Kind == TokenKind.Plus ? ExpressionKind.Add : ExpressionKind.Subtract;
            Advance();
            int right = ParseMultiplicative();
            left = EmitBinary(kind, left, right, opToken);
        }
        return left;
    }

    // 7. Multiplicative: * /
    private int ParseMultiplicative()
    {
        int left = ParseUnary();
        while (_current.Kind is TokenKind.Star or TokenKind.Slash)
        {
            var opToken = _current;
            var kind = opToken.Kind == TokenKind.Star ? ExpressionKind.Multiply : ExpressionKind.Divide;
            Advance();
            int right = ParseUnary();
            left = EmitBinary(kind, left, right, opToken);
        }
        return left;
    }

    // 8. Unary: ! -
    private int ParseUnary()
    {
        if (_current.Kind == TokenKind.Bang)
        {
            var opToken = _current;
            Advance();
            int operand = ParseUnary();
            var node = ExpressionNode.Default;
            node.Kind = ExpressionKind.Not;
            node.LeftIndex = operand;
            node.SourceOffset = opToken.Offset;
            node.SourceLength = opToken.Length;
            return Emit(node);
        }

        if (_current.Kind == TokenKind.Minus)
        {
            var opToken = _current;
            Advance();
            int operand = ParseUnary();
            var node = ExpressionNode.Default;
            node.Kind = ExpressionKind.Negate;
            node.LeftIndex = operand;
            node.SourceOffset = opToken.Offset;
            node.SourceLength = opToken.Length;
            return Emit(node);
        }

        return ParsePrimary();
    }

    // 9. Primary: literals, variables, fn(args), (expr)
    private int ParsePrimary()
    {
        var token = _current;

        switch (token.Kind)
        {
            case TokenKind.IntLiteral:
            {
                Advance();
                var node = ExpressionNode.Default;
                node.Kind = ExpressionKind.IntLiteral;
                node.LiteralValue = PropertyValue.FromInt(token.IntValue);
                node.LiteralKind = PropertyKind.Int;
                node.SourceOffset = token.Offset;
                node.SourceLength = token.Length;
                return Emit(node);
            }

            case TokenKind.FloatLiteral:
            {
                Advance();
                var node = ExpressionNode.Default;
                node.Kind = ExpressionKind.FloatLiteral;
                node.LiteralValue = PropertyValue.FromFloat(token.FloatValue);
                node.LiteralKind = PropertyKind.Float;
                node.SourceOffset = token.Offset;
                node.SourceLength = token.Length;
                return Emit(node);
            }

            case TokenKind.BoolLiteral:
            {
                Advance();
                var node = ExpressionNode.Default;
                node.Kind = ExpressionKind.BoolLiteral;
                node.LiteralValue = PropertyValue.FromBool(token.BoolValue);
                node.LiteralKind = PropertyKind.Bool;
                node.SourceOffset = token.Offset;
                node.SourceLength = token.Length;
                return Emit(node);
            }

            case TokenKind.StringLiteral:
            {
                Advance();
                var node = ExpressionNode.Default;
                node.Kind = ExpressionKind.StringLiteral;
                node.LiteralKind = PropertyKind.StringHandle;
                node.SourceOffset = token.Offset;
                node.SourceLength = token.Length;
                // Extract string content (strip quotes) and intern as StringHandle
                string content = ExtractStringContent(token);
                StringHandle handle = content;
                node.LiteralValue = PropertyValue.FromStringHandle(handle);
                return Emit(node);
            }

            case TokenKind.ColorLiteral:
            {
                Advance();
                var node = ExpressionNode.Default;
                node.Kind = ExpressionKind.ColorLiteral;
                uint argb = token.ColorValue;
                byte a = (byte)(argb >> 24);
                byte r = (byte)(argb >> 16);
                byte g = (byte)(argb >> 8);
                byte b = (byte)argb;
                node.LiteralValue = PropertyValue.FromColor32(new Core.Color32(r, g, b, a));
                node.LiteralKind = PropertyKind.Color32;
                node.SourceOffset = token.Offset;
                node.SourceLength = token.Length;
                return Emit(node);
            }

            case TokenKind.Variable:
            {
                Advance();
                // Strip '@' prefix for lookup
                string name = _source.Span.Slice(token.Offset + 1, token.Length - 1).ToString();
                int sourceIndex = -1;
                if (!_variableMap.TryGetValue(name, out sourceIndex))
                {
                    Error(token, $"Unknown variable '@{name}'");
                    sourceIndex = -1;
                }

                var node = ExpressionNode.Default;
                node.Kind = ExpressionKind.Variable;
                node.SourceIndex = sourceIndex;
                node.SourceOffset = token.Offset;
                node.SourceLength = token.Length;
                return Emit(node);
            }

            case TokenKind.Identifier:
            {
                // Could be a function call: name(args...)
                return ParseFunctionCall();
            }

            case TokenKind.LeftParen:
            {
                Advance();
                int expr = ParseTernary();
                Expect(TokenKind.RightParen, "Expected ')'");
                return expr;
            }

            default:
            {
                Error(token, $"Unexpected token '{token.Kind}'");
                Advance(); // skip to recover
                var node = ExpressionNode.Default;
                node.Kind = ExpressionKind.IntLiteral;
                node.LiteralValue = PropertyValue.FromInt(0);
                node.LiteralKind = PropertyKind.Int;
                node.SourceOffset = token.Offset;
                node.SourceLength = token.Length;
                return Emit(node);
            }
        }
    }

    private int ParseFunctionCall()
    {
        var nameToken = _current;
        ReadOnlySpan<char> name = TokenText(nameToken);
        Advance();

        if (_current.Kind != TokenKind.LeftParen)
        {
            Error(nameToken, $"Expected '(' after function name");
            var errNode = ExpressionNode.Default;
            errNode.Kind = ExpressionKind.IntLiteral;
            errNode.LiteralValue = PropertyValue.FromInt(0);
            errNode.LiteralKind = PropertyKind.Int;
            errNode.SourceOffset = nameToken.Offset;
            errNode.SourceLength = nameToken.Length;
            return Emit(errNode);
        }
        Advance(); // skip '('

        BuiltinFunction fn = ResolveFunction(name);
        if (fn == BuiltinFunction.None)
            Error(nameToken, $"Unknown function '{name.ToString()}'");

        // Parse arguments
        Span<int> args = stackalloc int[5];
        int argCount = 0;

        if (_current.Kind != TokenKind.RightParen)
        {
            args[argCount++] = ParseTernary();
            while (_current.Kind == TokenKind.Comma && argCount < 5)
            {
                Advance();
                args[argCount++] = ParseTernary();
            }
        }

        Expect(TokenKind.RightParen, "Expected ')' after function arguments");

        // Validate arity
        int expectedArity = GetExpectedArity(fn);
        if (expectedArity >= 0 && argCount != expectedArity)
            Error(nameToken, $"Function '{name.ToString()}' expects {expectedArity} argument(s), got {argCount}");

        var node = ExpressionNode.Default;
        node.Kind = ExpressionKind.FunctionCall;
        node.Function = fn;
        node.ArgCount = argCount;
        node.Arg0 = argCount > 0 ? args[0] : -1;
        node.Arg1 = argCount > 1 ? args[1] : -1;
        node.Arg2 = argCount > 2 ? args[2] : -1;
        node.Arg3 = argCount > 3 ? args[3] : -1;
        node.Arg4 = argCount > 4 ? args[4] : -1;
        node.SourceOffset = nameToken.Offset;
        node.SourceLength = nameToken.Length;
        return Emit(node);
    }

    private int EmitBinary(ExpressionKind kind, int left, int right, ExpressionToken opToken)
    {
        var node = ExpressionNode.Default;
        node.Kind = kind;
        node.LeftIndex = left;
        node.RightIndex = right;
        node.SourceOffset = opToken.Offset;
        node.SourceLength = opToken.Length;
        return Emit(node);
    }

    private static BuiltinFunction ResolveFunction(ReadOnlySpan<char> name)
    {
        if (name.SequenceEqual("min".AsSpan())) return BuiltinFunction.Min;
        if (name.SequenceEqual("max".AsSpan())) return BuiltinFunction.Max;
        if (name.SequenceEqual("clamp".AsSpan())) return BuiltinFunction.Clamp;
        if (name.SequenceEqual("lerp".AsSpan())) return BuiltinFunction.Lerp;
        if (name.SequenceEqual("remap".AsSpan())) return BuiltinFunction.Remap;
        if (name.SequenceEqual("abs".AsSpan())) return BuiltinFunction.Abs;
        if (name.SequenceEqual("floor".AsSpan())) return BuiltinFunction.Floor;
        if (name.SequenceEqual("ceil".AsSpan())) return BuiltinFunction.Ceil;
        if (name.SequenceEqual("round".AsSpan())) return BuiltinFunction.Round;
        return BuiltinFunction.None;
    }

    private static int GetExpectedArity(BuiltinFunction fn)
    {
        return fn switch
        {
            BuiltinFunction.Abs => 1,
            BuiltinFunction.Floor => 1,
            BuiltinFunction.Ceil => 1,
            BuiltinFunction.Round => 1,
            BuiltinFunction.Min => 2,
            BuiltinFunction.Max => 2,
            BuiltinFunction.Clamp => 3,
            BuiltinFunction.Lerp => 3,
            BuiltinFunction.Remap => 5,
            _ => -1,
        };
    }
}

public readonly struct ExpressionParseResult
{
    public readonly ExpressionNode[] Nodes;
    public readonly int RootIndex;
    public readonly ExpressionDiagnostic[] Diagnostics;

    public ExpressionParseResult(ExpressionNode[] nodes, int rootIndex, ExpressionDiagnostic[] diagnostics)
    {
        Nodes = nodes;
        RootIndex = rootIndex;
        Diagnostics = diagnostics;
    }

    public bool HasErrors => Diagnostics.Length > 0;
}
