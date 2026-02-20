using System;
using DerpLib.Text;

namespace DerpLib.ImGui.Core;

internal static class ImTextEdit
{
    public static void InsertChar(Span<char> buffer, ref int length, int maxLength, int pos, char c)
    {
        if (length >= maxLength)
        {
            return;
        }

        if ((uint)pos > (uint)length)
        {
            pos = length;
        }

        for (int i = length; i > pos; i--)
        {
            buffer[i] = buffer[i - 1];
        }
        buffer[pos] = c;
        length++;
    }

    public static void DeleteRange(Span<char> buffer, ref int length, int start, int end)
    {
        if (start < 0) start = 0;
        if (end > length) end = length;
        if (end <= start)
        {
            return;
        }

        int deleteCount = end - start;
        for (int i = start; i < length - deleteCount; i++)
        {
            buffer[i] = buffer[i + deleteCount];
        }
        length -= deleteCount;
    }

    public static int GetCaretPosFromRelativeX(Font? font, ReadOnlySpan<char> text, float fontSize, float letterSpacingPx, float relativeX)
    {
        if (relativeX <= 0f || text.Length == 0)
        {
            return 0;
        }

        if (font == null)
        {
            // Rough fallback: fixed width characters.
            int estimate = (int)(relativeX / 7f);
            if (estimate < 0) estimate = 0;
            if (estimate > text.Length) estimate = text.Length;
            return estimate;
        }

        float scale = fontSize / font.BaseSizePixels;
        float cursorX = 0f;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            float advanceX;

            if (font.TryGetGlyph(c, out var glyph))
            {
                advanceX = glyph.AdvanceX * scale + letterSpacingPx;
            }
            else if (font.TryGetGlyph(' ', out var spaceGlyph))
            {
                advanceX = spaceGlyph.AdvanceX * scale + letterSpacingPx;
            }
            else
            {
                continue;
            }

            float nextX = cursorX + advanceX;
            if (nextX >= relativeX)
            {
                // Snap to nearest side of the glyph advance.
                return (relativeX - cursorX) < (nextX - relativeX) ? i : i + 1;
            }

            cursorX = nextX;
        }

        return text.Length;
    }
}
