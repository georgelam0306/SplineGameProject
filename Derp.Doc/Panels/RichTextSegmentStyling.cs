using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.Text;
using Derp.Doc.Model;

namespace Derp.Doc.Panels;

internal static class RichTextSegmentStyling
{
    private static Font? _boldStyleFont;
    private static Font? _italicStyleFont;
    private static Font? _boldItalicStyleFont;

    public static void SetStyleFonts(Font? boldFont, Font? italicFont, Font? boldItalicFont)
    {
        _boldStyleFont = boldFont;
        _italicStyleFont = italicFont;
        _boldItalicStyleFont = boldItalicFont;
    }

    public static uint GetStyleColor(RichSpanStyle spanStyle, uint defaultColor, ImStyle style)
    {
        if ((spanStyle & RichSpanStyle.Code) != 0)
        {
            return 0xFF77BBDD;
        }

        if ((spanStyle & RichSpanStyle.Bold) != 0)
        {
            return 0xFFFFFFFF;
        }

        if ((spanStyle & RichSpanStyle.Italic) != 0)
        {
            return style.TextSecondary;
        }

        return defaultColor;
    }

    public static Font? ResolveSegmentFont(RichSpanStyle style, ReadOnlySpan<char> segmentText)
    {
        bool hasBold = (style & RichSpanStyle.Bold) != 0;
        bool hasItalic = (style & RichSpanStyle.Italic) != 0;

        Font? runtimeBoldFont = Im.Context.Font?.BoldVariant;
        Font? runtimeItalicFont = Im.Context.Font?.ItalicVariant;
        Font? runtimeBoldItalicFont = Im.Context.Font?.BoldItalicVariant;

        if (hasBold && hasItalic)
        {
            Font? candidateBoldItalic = _boldItalicStyleFont;
            if (CanRenderSegmentWithFont(segmentText, candidateBoldItalic))
            {
                return candidateBoldItalic;
            }

            Font? candidateRuntimeBoldItalic = runtimeBoldItalicFont;
            if (CanRenderSegmentWithFont(segmentText, candidateRuntimeBoldItalic))
            {
                return candidateRuntimeBoldItalic;
            }

            Font? candidateBold = _boldStyleFont;
            if (CanRenderSegmentWithFont(segmentText, candidateBold))
            {
                return candidateBold;
            }

            Font? candidateRuntimeBold = runtimeBoldFont;
            if (CanRenderSegmentWithFont(segmentText, candidateRuntimeBold))
            {
                return candidateRuntimeBold;
            }

            Font? candidateItalic = _italicStyleFont;
            if (CanRenderSegmentWithFont(segmentText, candidateItalic))
            {
                return candidateItalic;
            }

            Font? candidateRuntimeItalic = runtimeItalicFont;
            if (CanRenderSegmentWithFont(segmentText, candidateRuntimeItalic))
            {
                return candidateRuntimeItalic;
            }

            return null;
        }

        if (hasBold)
        {
            Font? candidateBold = _boldStyleFont;
            if (CanRenderSegmentWithFont(segmentText, candidateBold))
            {
                return candidateBold;
            }

            Font? candidateRuntimeBold = runtimeBoldFont;
            if (CanRenderSegmentWithFont(segmentText, candidateRuntimeBold))
            {
                return candidateRuntimeBold;
            }

            return null;
        }

        if (hasItalic)
        {
            Font? candidateItalic = _italicStyleFont;
            if (CanRenderSegmentWithFont(segmentText, candidateItalic))
            {
                return candidateItalic;
            }

            Font? candidateRuntimeItalic = runtimeItalicFont;
            if (CanRenderSegmentWithFont(segmentText, candidateRuntimeItalic))
            {
                return candidateRuntimeItalic;
            }

            return null;
        }

        return null;
    }

    public static float MeasureSegmentWidth(ReadOnlySpan<char> text, float fontSize, Font? segmentFont)
    {
        if (segmentFont == null)
        {
            return Im.MeasureTextWidth(text, fontSize);
        }

        return segmentFont.MeasureText(text, fontSize, 0f).X;
    }

    public static void DrawSegmentText(ReadOnlySpan<char> text, float x, float y, float fontSize,
        uint color, RichSpanStyle style, Font? segmentFont)
    {
        if (segmentFont == null)
        {
            bool needsFakeBold = (style & RichSpanStyle.Bold) != 0;
            if (needsFakeBold)
            {
                Im.Text(text, x + 1f, y, fontSize, color);
            }

            Im.Text(text, x, y, fontSize, color);
            return;
        }

        var context = Im.Context;
        Font? previousPrimaryFont = context.Font;
        Font? previousSecondaryFont = context.SecondaryFont;
        if (previousPrimaryFont == null)
        {
            return;
        }

        if (ReferenceEquals(previousPrimaryFont, segmentFont))
        {
            Im.Text(text, x, y, fontSize, color);
            return;
        }

        if (previousSecondaryFont == null)
        {
            context.SetFont(segmentFont);
            Im.Text(text, x, y, fontSize, color);
            context.SetFont(previousPrimaryFont);
        }
        else
        {
            context.SetFonts(segmentFont, previousSecondaryFont);
            Im.Text(text, x, y, fontSize, color);
            context.SetFonts(previousPrimaryFont, previousSecondaryFont);
        }
    }

    private static bool CanRenderSegmentWithFont(ReadOnlySpan<char> segmentText, Font? font)
    {
        if (font == null)
        {
            return false;
        }

        for (int index = 0; index < segmentText.Length; index++)
        {
            char currentChar = segmentText[index];
            if (char.IsWhiteSpace(currentChar))
            {
                continue;
            }

            if (!font.TryGetGlyph(currentChar, out _))
            {
                return false;
            }
        }

        return true;
    }
}
