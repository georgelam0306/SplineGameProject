using System;
using DerpLib.Text;

namespace DerpLib.ImGui.Core;

internal static class ImTextMetrics
{
    public static float MeasureWidth(Font? font, ReadOnlySpan<char> text, float fontSize, float letterSpacingPx = 0f)
    {
        return MeasureWidth(font, secondaryFont: null, text, fontSize, letterSpacingPx);
    }

    public static float MeasureWidth(Font? primaryFont, Font? secondaryFont, ReadOnlySpan<char> text, float fontSize, float letterSpacingPx = 0f)
    {
        if (text.Length == 0)
        {
            return 0f;
        }

        if (primaryFont == null && secondaryFont == null)
        {
            return Math.Min(text.Length, 60) * 7f;
        }

        float primaryScale = primaryFont == null ? 0f : fontSize / primaryFont.BaseSizePixels;
        float secondaryScale = secondaryFont == null ? 0f : fontSize / secondaryFont.BaseSizePixels;
        float width = 0f;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bool preferSecondary = PreferSecondaryFontForCodepoint(c);
            if (preferSecondary &&
                secondaryFont != null &&
                secondaryFont.TryGetGlyph(c, out var preferredSecondaryGlyph))
            {
                width += preferredSecondaryGlyph.AdvanceX * secondaryScale + letterSpacingPx;
            }
            else if (primaryFont != null && primaryFont.TryGetGlyph(c, out var glyph))
            {
                width += glyph.AdvanceX * primaryScale + letterSpacingPx;
            }
            else if (secondaryFont != null && secondaryFont.TryGetGlyph(c, out glyph))
            {
                width += glyph.AdvanceX * secondaryScale + letterSpacingPx;
            }
            else if (primaryFont != null && primaryFont.TryGetGlyph(' ', out var spaceGlyph))
            {
                width += spaceGlyph.AdvanceX * primaryScale + letterSpacingPx;
            }
            else if (secondaryFont != null && secondaryFont.TryGetGlyph(' ', out spaceGlyph))
            {
                width += spaceGlyph.AdvanceX * secondaryScale + letterSpacingPx;
            }
        }

        return width;
    }

    private static bool PreferSecondaryFontForCodepoint(char codepoint)
    {
        return codepoint >= '\ue000' && codepoint <= '\uf8ff';
    }
}
