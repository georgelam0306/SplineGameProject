using System;
using System.Globalization;

namespace DerpLib.Ecs.Editor;

/// <summary>
/// Lexer for the expression DSL. Operates on <see cref="ReadOnlyMemory{T}"/>
/// so it can be stored as a field in non-ref-struct types.
/// </summary>
public struct ExpressionLexer
{
    private readonly ReadOnlyMemory<char> _source;
    private int _pos;

    public ExpressionLexer(ReadOnlyMemory<char> source)
    {
        _source = source;
        _pos = 0;
    }

    public ExpressionToken NextToken()
    {
        var span = _source.Span;
        SkipWhitespace(span);

        if (_pos >= span.Length)
            return new ExpressionToken(TokenKind.End, _pos, 0);

        char c = span[_pos];

        // Single-character tokens
        switch (c)
        {
            case '(': return SingleChar(TokenKind.LeftParen);
            case ')': return SingleChar(TokenKind.RightParen);
            case ',': return SingleChar(TokenKind.Comma);
            case '+': return SingleChar(TokenKind.Plus);
            case '-': return SingleChar(TokenKind.Minus);
            case '*': return SingleChar(TokenKind.Star);
            case '/': return SingleChar(TokenKind.Slash);
            case '?': return SingleChar(TokenKind.Question);
            case ':': return SingleChar(TokenKind.Colon);
        }

        // Two-character tokens
        if (c == '=' && Peek(span, 1) == '=') return TwoChar(TokenKind.EqualEqual);
        if (c == '!' && Peek(span, 1) == '=') return TwoChar(TokenKind.BangEqual);
        if (c == '<' && Peek(span, 1) == '=') return TwoChar(TokenKind.LessEqual);
        if (c == '>' && Peek(span, 1) == '=') return TwoChar(TokenKind.GreaterEqual);
        if (c == '&' && Peek(span, 1) == '&') return TwoChar(TokenKind.AmpAmp);
        if (c == '|' && Peek(span, 1) == '|') return TwoChar(TokenKind.PipePipe);

        // Single < > !
        if (c == '<') return SingleChar(TokenKind.Less);
        if (c == '>') return SingleChar(TokenKind.Greater);
        if (c == '!') return SingleChar(TokenKind.Bang);

        // Variable: @identifier or @identifier.identifier
        if (c == '@') return LexVariable(span);

        // Color literal: #RRGGBB or #RRGGBBAA
        if (c == '#') return LexColor(span);

        // String literal: "..."
        if (c == '"') return LexString(span);

        // Number literal
        if (char.IsDigit(c)) return LexNumber(span);

        // Identifier (function names, bool keywords)
        if (char.IsLetter(c) || c == '_') return LexIdentifier(span);

        // Unknown character
        return new ExpressionToken(TokenKind.Error, _pos++, 1);
    }

    public ExpressionToken PeekToken()
    {
        int saved = _pos;
        var token = NextToken();
        _pos = saved;
        return token;
    }

    private void SkipWhitespace(ReadOnlySpan<char> span)
    {
        while (_pos < span.Length && char.IsWhiteSpace(span[_pos]))
            _pos++;
    }

    private char Peek(ReadOnlySpan<char> span, int offset)
    {
        int index = _pos + offset;
        return index < span.Length ? span[index] : '\0';
    }

    private ExpressionToken SingleChar(TokenKind kind)
    {
        return new ExpressionToken(kind, _pos++, 1);
    }

    private ExpressionToken TwoChar(TokenKind kind)
    {
        var token = new ExpressionToken(kind, _pos, 2);
        _pos += 2;
        return token;
    }

    private ExpressionToken LexVariable(ReadOnlySpan<char> span)
    {
        int start = _pos;
        _pos++; // skip '@'

        // Read identifier: letters, digits, underscores, dots (for paths like @player.name)
        while (_pos < span.Length && (char.IsLetterOrDigit(span[_pos]) || span[_pos] == '_' || span[_pos] == '.'))
            _pos++;

        if (_pos - start == 1)
            return new ExpressionToken(TokenKind.Error, start, 1);

        return new ExpressionToken(TokenKind.Variable, start, _pos - start);
    }

    private ExpressionToken LexColor(ReadOnlySpan<char> span)
    {
        int start = _pos;
        _pos++; // skip '#'

        int hexStart = _pos;
        while (_pos < span.Length && IsHexDigit(span[_pos]))
            _pos++;

        int hexLen = _pos - hexStart;

        // Parse 6 (RGB) or 8 (RGBA) hex digits
        if (hexLen == 6 || hexLen == 8)
        {
            ReadOnlySpan<char> hex = span.Slice(hexStart, hexLen);
            uint value = 0;
            for (int i = 0; i < hexLen; i++)
            {
                value = (value << 4) | HexVal(hex[i]);
            }

            if (hexLen == 6)
                value = 0xFF000000 | value; // Add full alpha

            return new ExpressionToken(TokenKind.ColorLiteral, start, _pos - start, value);
        }

        return new ExpressionToken(TokenKind.Error, start, _pos - start);
    }

    private ExpressionToken LexString(ReadOnlySpan<char> span)
    {
        int start = _pos;
        _pos++; // skip opening '"'

        while (_pos < span.Length && span[_pos] != '"')
        {
            if (span[_pos] == '\\' && _pos + 1 < span.Length)
                _pos++; // skip escaped character
            _pos++;
        }

        if (_pos >= span.Length)
            return new ExpressionToken(TokenKind.Error, start, _pos - start);

        _pos++; // skip closing '"'
        return new ExpressionToken(TokenKind.StringLiteral, start, _pos - start);
    }

    private ExpressionToken LexNumber(ReadOnlySpan<char> span)
    {
        int start = _pos;
        bool hasDecimal = false;

        while (_pos < span.Length && (char.IsDigit(span[_pos]) || span[_pos] == '.'))
        {
            if (span[_pos] == '.')
            {
                if (hasDecimal) break; // second dot → stop
                hasDecimal = true;
            }
            _pos++;
        }

        ReadOnlySpan<char> numSpan = span.Slice(start, _pos - start);

        if (hasDecimal)
        {
            if (float.TryParse(numSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out float fVal))
                return new ExpressionToken(TokenKind.FloatLiteral, start, _pos - start, fVal);
        }
        else
        {
            if (int.TryParse(numSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iVal))
                return new ExpressionToken(TokenKind.IntLiteral, start, _pos - start, iVal);
        }

        return new ExpressionToken(TokenKind.Error, start, _pos - start);
    }

    private ExpressionToken LexIdentifier(ReadOnlySpan<char> span)
    {
        int start = _pos;
        while (_pos < span.Length && (char.IsLetterOrDigit(span[_pos]) || span[_pos] == '_'))
            _pos++;

        ReadOnlySpan<char> word = span.Slice(start, _pos - start);

        // Bool keywords
        if (word.SequenceEqual("true".AsSpan()))
            return new ExpressionToken(TokenKind.BoolLiteral, start, _pos - start, true);
        if (word.SequenceEqual("false".AsSpan()))
            return new ExpressionToken(TokenKind.BoolLiteral, start, _pos - start, false);

        return new ExpressionToken(TokenKind.Identifier, start, _pos - start);
    }

    private static bool IsHexDigit(char c)
    {
        return c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F';
    }

    private static uint HexVal(char c)
    {
        if (c >= '0' && c <= '9') return (uint)(c - '0');
        if (c >= 'a' && c <= 'f') return (uint)(c - 'a' + 10);
        return (uint)(c - 'A' + 10);
    }
}
