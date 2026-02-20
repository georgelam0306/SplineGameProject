using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.Text;
using Derp.Doc.Model;

namespace Derp.Doc.Panels;

/// <summary>
/// Renders rich text with per-span colors and decorations.
/// </summary>
internal static class RichTextRenderer
{
    private static RichTextLayout.VisualLine[] _visualLinesScratch = new RichTextLayout.VisualLine[512];
    private static RichTextLayout.StyledSegment[] _styledSegmentsScratch = new RichTextLayout.StyledSegment[256];

    public static void SetStyleFonts(Font? boldFont, Font? italicFont, Font? boldItalicFont)
    {
        RichTextSegmentStyling.SetStyleFonts(boldFont, italicFont, boldItalicFont);
    }

    /// <summary>
    /// Draws plain text with newline and word-wrap support. Used for non-focused blocks.
    /// </summary>
    public static void DrawPlain(string text, float x, float y, float maxWidth, float fontSize, uint color)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var textSpan = text.AsSpan();
        float lineHeight = fontSize * 1.4f;
        RichTextLayout.EnsureVisualLineCapacity(ref _visualLinesScratch, textSpan.Length + 1);
        int visualLineCount = RichTextLayout.BuildVisualLines(
            Im.Context.Font,
            textSpan,
            textSpan.Length,
            fontSize,
            true,
            maxWidth,
            0f,
            _visualLinesScratch);

        for (int lineIndex = 0; lineIndex < visualLineCount; lineIndex++)
        {
            var line = _visualLinesScratch[lineIndex];
            if (line.Length <= 0)
            {
                continue;
            }

            float lineY = y + lineIndex * lineHeight;
            Im.Text(textSpan.Slice(line.Start, line.Length), x, lineY, fontSize, color);
        }
    }

    /// <summary>
    /// Draws rich text with per-span styling.
    /// </summary>
    public static void Draw(RichText richText, float x, float y, float maxWidth, float fontSize, uint defaultColor)
    {
        if (string.IsNullOrEmpty(richText.PlainText))
        {
            return;
        }

        var style = Im.Style;
        ReadOnlySpan<char> text = richText.PlainText.AsSpan();
        float lineHeight = fontSize * 1.4f;

        RichTextLayout.EnsureVisualLineCapacity(ref _visualLinesScratch, text.Length + 1);
        int visualLineCount = RichTextLayout.BuildVisualLines(
            Im.Context.Font,
            text,
            text.Length,
            fontSize,
            true,
            maxWidth,
            0f,
            _visualLinesScratch);

        bool hasAnySpans = richText.Spans.Count > 0;
        if (hasAnySpans)
        {
            RichTextLayout.EnsureStyledSegmentCapacity(ref _styledSegmentsScratch, richText.Spans.Count * 2 + 2);
        }

        for (int lineIndex = 0; lineIndex < visualLineCount; lineIndex++)
        {
            var line = _visualLinesScratch[lineIndex];
            if (line.Length <= 0)
            {
                continue;
            }

            float lineY = y + lineIndex * lineHeight;
            int lineStart = line.Start;
            int lineEnd = line.Start + line.Length;

            bool hasLineSpans = false;
            if (hasAnySpans)
            {
                for (int spanIndex = 0; spanIndex < richText.Spans.Count; spanIndex++)
                {
                    var span = richText.Spans[spanIndex];
                    if (span.Start < lineEnd && span.End > lineStart)
                    {
                        hasLineSpans = true;
                        break;
                    }
                }
            }

            if (!hasLineSpans)
            {
                Im.Text(text.Slice(lineStart, line.Length), x, lineY, fontSize, defaultColor);
                continue;
            }

            int segmentCount = RichTextLayout.BuildLineSegments(richText.Spans, lineStart, line.Length, _styledSegmentsScratch);
            float curX = x;
            for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
            {
                var segment = _styledSegmentsScratch[segmentIndex];
                if (segment.End <= segment.Start)
                {
                    continue;
                }

                ReadOnlySpan<char> segmentText = text.Slice(segment.Start, segment.End - segment.Start);
                RichSpanStyle segmentStyle = segment.Style;
                Font? segmentFont = RichTextSegmentStyling.ResolveSegmentFont(segmentStyle, segmentText);
                float segmentWidth = RichTextSegmentStyling.MeasureSegmentWidth(segmentText, fontSize, segmentFont);

                if ((segmentStyle & RichSpanStyle.Code) != 0)
                {
                    Im.DrawRoundedRect(curX - 2f, lineY - 1f, segmentWidth + 4f, fontSize + 2f, 2f, style.Surface);
                }
                else if ((segmentStyle & RichSpanStyle.Highlight) != 0)
                {
                    Im.DrawRect(curX - 1f, lineY, segmentWidth + 2f, fontSize, 0x4000BBFF);
                }

                uint color = RichTextSegmentStyling.GetStyleColor(segmentStyle, defaultColor, style);
                RichTextSegmentStyling.DrawSegmentText(segmentText, curX, lineY, fontSize, color, segmentStyle, segmentFont);

                if ((segmentStyle & RichSpanStyle.Strikethrough) != 0)
                {
                    float lineY2 = lineY + fontSize * 0.5f;
                    Im.DrawLine(curX, lineY2, curX + segmentWidth, lineY2, 1f, color);
                }

                if ((segmentStyle & RichSpanStyle.Underline) != 0)
                {
                    float lineY2 = lineY + fontSize + 1f;
                    Im.DrawLine(curX, lineY2, curX + segmentWidth, lineY2, 1f, color);
                }

                curX += segmentWidth;
            }
        }
    }
}
