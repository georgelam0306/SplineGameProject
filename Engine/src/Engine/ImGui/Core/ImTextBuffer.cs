using System;

namespace DerpLib.ImGui.Core;

/// <summary>
/// Growable UTF-16 character buffer for text editing widgets.
/// Allocates only when capacity must grow.
/// </summary>
public sealed class ImTextBuffer
{
    private char[] _buffer;

    public int Length { get; private set; }
    public int Capacity => _buffer.Length;

    public ImTextBuffer(int initialCapacity = 256)
    {
        if (initialCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        }

        _buffer = initialCapacity == 0 ? Array.Empty<char>() : new char[initialCapacity];
        Length = 0;
    }

    public ReadOnlySpan<char> AsSpan()
    {
        return _buffer.AsSpan(0, Length);
    }

    public void Clear()
    {
        Length = 0;
    }

    public void SetText(ReadOnlySpan<char> text)
    {
        EnsureCapacity(text.Length);
        text.CopyTo(_buffer);
        Length = text.Length;
    }

    public void InsertChar(int pos, char c)
    {
        EnsureCapacity(Length + 1);

        if ((uint)pos > (uint)Length)
        {
            pos = Length;
        }

        for (int i = Length; i > pos; i--)
        {
            _buffer[i] = _buffer[i - 1];
        }

        _buffer[pos] = c;
        Length++;
    }

    public void InsertText(int pos, ReadOnlySpan<char> text)
    {
        if (text.Length == 0)
        {
            return;
        }

        EnsureCapacity(Length + text.Length);

        if ((uint)pos > (uint)Length)
        {
            pos = Length;
        }

        for (int i = Length - 1; i >= pos; i--)
        {
            _buffer[i + text.Length] = _buffer[i];
        }

        text.CopyTo(_buffer.AsSpan(pos, text.Length));
        Length += text.Length;
    }

    public void DeleteRange(int start, int end)
    {
        if (start < 0) start = 0;
        if (end > Length) end = Length;
        if (end <= start)
        {
            return;
        }

        int deleteCount = end - start;
        for (int i = start; i < Length - deleteCount; i++)
        {
            _buffer[i] = _buffer[i + deleteCount];
        }

        Length -= deleteCount;
    }

    private void EnsureCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= _buffer.Length)
        {
            return;
        }

        int newCapacity = _buffer.Length == 0 ? 16 : _buffer.Length;
        while (newCapacity < requiredCapacity)
        {
            newCapacity *= 2;
        }

        var newBuffer = new char[newCapacity];
        if (Length > 0)
        {
            _buffer.AsSpan(0, Length).CopyTo(newBuffer);
        }

        _buffer = newBuffer;
    }
}
