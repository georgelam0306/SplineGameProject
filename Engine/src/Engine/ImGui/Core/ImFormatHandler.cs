using System.Buffers;
using System.Runtime.CompilerServices;

namespace DerpLib.ImGui.Core;

/// <summary>
/// Zero-allocation string interpolation handler for ImGUI.
/// Uses a thread-static scratch buffer to avoid heap allocations.
/// </summary>
[InterpolatedStringHandler]
public ref struct ImFormatHandler
{
    private const int MaxLength = 256;

    [ThreadStatic]
    private static char[]? _scratchBuffer;

    private Span<char> _buffer;
    private int _position;

    public ImFormatHandler(int literalLength, int formattedCount)
    {
        _scratchBuffer ??= new char[MaxLength];
        _buffer = _scratchBuffer;
        _position = 0;
    }

    public void AppendLiteral(string s)
    {
        if (s.Length > _buffer.Length - _position)
            return; // Truncate if too long

        s.AsSpan().CopyTo(_buffer.Slice(_position));
        _position += s.Length;
    }

    public void AppendFormatted<T>(T value)
    {
        if (value is ISpanFormattable spanFormattable)
        {
            if (spanFormattable.TryFormat(_buffer.Slice(_position), out int charsWritten, default, null))
            {
                _position += charsWritten;
            }
        }
        else if (value is string str)
        {
            AppendLiteral(str);
        }
        else if (value != null)
        {
            // Fallback - this allocates but handles edge cases
            var str2 = value.ToString();
            if (str2 != null)
                AppendLiteral(str2);
        }
    }

    public void AppendFormatted<T>(T value, string? format) where T : ISpanFormattable
    {
        if (value.TryFormat(_buffer.Slice(_position), out int charsWritten, format, null))
        {
            _position += charsWritten;
        }
    }

    public void AppendFormatted(ReadOnlySpan<char> value)
    {
        if (value.Length > _buffer.Length - _position)
            return;

        value.CopyTo(_buffer.Slice(_position));
        _position += value.Length;
    }

    public void AppendFormatted(string? value)
    {
        if (value != null)
            AppendLiteral(value);
    }

    public void AppendFormatted(int value)
    {
        if (value.TryFormat(_buffer.Slice(_position), out int charsWritten))
            _position += charsWritten;
    }

    public void AppendFormatted(int value, string? format)
    {
        if (value.TryFormat(_buffer.Slice(_position), out int charsWritten, format))
            _position += charsWritten;
    }

    public void AppendFormatted(uint value)
    {
        if (value.TryFormat(_buffer.Slice(_position), out int charsWritten))
            _position += charsWritten;
    }

    public void AppendFormatted(long value)
    {
        if (value.TryFormat(_buffer.Slice(_position), out int charsWritten))
            _position += charsWritten;
    }

    public void AppendFormatted(float value)
    {
        if (value.TryFormat(_buffer.Slice(_position), out int charsWritten))
            _position += charsWritten;
    }

    public void AppendFormatted(float value, string? format)
    {
        if (value.TryFormat(_buffer.Slice(_position), out int charsWritten, format))
            _position += charsWritten;
    }

    public void AppendFormatted(double value)
    {
        if (value.TryFormat(_buffer.Slice(_position), out int charsWritten))
            _position += charsWritten;
    }

    public void AppendFormatted(double value, string? format)
    {
        if (value.TryFormat(_buffer.Slice(_position), out int charsWritten, format))
            _position += charsWritten;
    }

    public void AppendFormatted(bool value)
    {
        var str = value ? "True" : "False";
        AppendLiteral(str);
    }

    public void AppendFormatted(char value)
    {
        if (_position < _buffer.Length)
        {
            _buffer[_position++] = value;
        }
    }

    /// <summary>
    /// Get the formatted text as a ReadOnlySpan.
    /// </summary>
    public ReadOnlySpan<char> Text => _buffer.Slice(0, _position);

    /// <summary>
    /// Get the length of the formatted text.
    /// </summary>
    public int Length => _position;
}
