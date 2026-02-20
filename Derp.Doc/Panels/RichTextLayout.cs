using DerpLib.Text;
using Derp.Doc.Model;

namespace Derp.Doc.Panels;

internal static class RichTextLayout
{
    private const int StyleBitCount = 6;
    private static int[] _segmentBoundariesScratch = new int[256];
    private static RichSpanStyle[] _segmentStylesScratch = new RichSpanStyle[256];
    private static int[] _segmentStyleDeltaScratch = new int[256 * StyleBitCount];
    private static readonly RichSpanStyle[] StyleBits =
    [
        RichSpanStyle.Bold,
        RichSpanStyle.Italic,
        RichSpanStyle.Code,
        RichSpanStyle.Strikethrough,
        RichSpanStyle.Underline,
        RichSpanStyle.Highlight,
    ];

    internal struct VisualLine
    {
        public int Start;
        public int Length;
    }

    internal struct StyledSegment
    {
        public int Start;
        public int End;
        public RichSpanStyle Style;
    }

    internal static void EnsureVisualLineCapacity(ref VisualLine[] lines, int requiredCapacity)
    {
        if (requiredCapacity <= lines.Length)
        {
            return;
        }

        int newCapacity = lines.Length;
        while (newCapacity < requiredCapacity)
        {
            newCapacity *= 2;
        }

        Array.Resize(ref lines, newCapacity);
    }

    internal static void EnsureStyledSegmentCapacity(ref StyledSegment[] segments, int requiredCapacity)
    {
        if (requiredCapacity <= segments.Length)
        {
            return;
        }

        int newCapacity = segments.Length;
        while (newCapacity < requiredCapacity)
        {
            newCapacity *= 2;
        }

        Array.Resize(ref segments, newCapacity);
    }

    internal static int BuildVisualLines(
        Font? font,
        ReadOnlySpan<char> buffer,
        int length,
        float fontSize,
        bool wordWrap,
        float wrapWidth,
        float letterSpacingPx,
        VisualLine[] output)
    {
        int count = 0;
        int lineStart = 0;

        float fallbackAdvanceX = font != null && font.TryGetGlyph(' ', out var spaceGlyph)
            ? spaceGlyph.AdvanceX * (fontSize / font.BaseSizePixels)
            : 7f;

        float scale = font != null ? (fontSize / font.BaseSizePixels) : 1f;
        float currentWidth = 0f;
        int lastBreakIndex = -1;

        float effectiveWrapWidth = wordWrap ? Math.Max(1f, wrapWidth) : float.PositiveInfinity;

        for (int index = 0; index < length; index++)
        {
            char currentChar = buffer[index];
            if (currentChar == '\r')
            {
                continue;
            }

            if (currentChar == '\n')
            {
                if (count >= output.Length)
                {
                    return output.Length;
                }

                output[count++] = new VisualLine { Start = lineStart, Length = index - lineStart };
                lineStart = index + 1;
                currentWidth = 0f;
                lastBreakIndex = -1;
                continue;
            }

            float advanceX = GetAdvanceX(font, scale, fallbackAdvanceX, currentChar) + letterSpacingPx;

            if (wordWrap && currentWidth + advanceX > effectiveWrapWidth && index > lineStart)
            {
                int wrapEnd = index;
                if (lastBreakIndex >= lineStart)
                {
                    wrapEnd = (lastBreakIndex == index) ? index : (lastBreakIndex + 1);
                }

                if (wrapEnd <= lineStart)
                {
                    wrapEnd = index;
                }

                if (count >= output.Length)
                {
                    return output.Length;
                }

                output[count++] = new VisualLine { Start = lineStart, Length = wrapEnd - lineStart };
                lineStart = wrapEnd;
                currentWidth = 0f;
                lastBreakIndex = -1;
                index = lineStart - 1;
                continue;
            }

            currentWidth += advanceX;

            if (currentChar == ' ' || currentChar == '\t')
            {
                lastBreakIndex = index;
            }
        }

        if (count < output.Length)
        {
            output[count++] = new VisualLine { Start = lineStart, Length = length - lineStart };
        }

        if (count == 0)
        {
            output[0] = new VisualLine { Start = 0, Length = 0 };
            count = 1;
        }

        return count;
    }

    internal static int BuildLineSegments(
        List<RichSpan> spans,
        int lineStart,
        int lineLength,
        StyledSegment[] output)
    {
        int lineEnd = lineStart + lineLength;
        EnsureSegmentBoundaryCapacity(spans.Count * 2 + 2);

        int boundaryCount = 0;
        _segmentBoundariesScratch[boundaryCount++] = lineStart;
        _segmentBoundariesScratch[boundaryCount++] = lineEnd;

        for (int spanIndex = 0; spanIndex < spans.Count; spanIndex++)
        {
            var span = spans[spanIndex];
            int segmentStart = Math.Max(span.Start, lineStart);
            int segmentEnd = Math.Min(span.End, lineEnd);
            if (segmentStart < segmentEnd)
            {
                _segmentBoundariesScratch[boundaryCount++] = segmentStart;
                _segmentBoundariesScratch[boundaryCount++] = segmentEnd;
            }
        }

        Array.Sort(_segmentBoundariesScratch, 0, boundaryCount);

        int uniqueBoundaryCount = 1;
        for (int boundaryIndex = 1; boundaryIndex < boundaryCount; boundaryIndex++)
        {
            if (_segmentBoundariesScratch[boundaryIndex] != _segmentBoundariesScratch[uniqueBoundaryCount - 1])
            {
                _segmentBoundariesScratch[uniqueBoundaryCount++] = _segmentBoundariesScratch[boundaryIndex];
            }
        }

        BuildSegmentStyles(spans, lineStart, lineEnd, uniqueBoundaryCount);

        int outputCount = 0;
        for (int segmentIndex = 0; segmentIndex < uniqueBoundaryCount - 1; segmentIndex++)
        {
            int segmentStart = _segmentBoundariesScratch[segmentIndex];
            int segmentEnd = _segmentBoundariesScratch[segmentIndex + 1];
            if (segmentEnd <= segmentStart)
            {
                continue;
            }

            output[outputCount++] = new StyledSegment
            {
                Start = segmentStart,
                End = segmentEnd,
                Style = _segmentStylesScratch[segmentIndex],
            };
        }

        return outputCount;
    }

    private static float GetAdvanceX(Font? font, float scale, float fallbackAdvanceX, char currentChar)
    {
        if (currentChar == '\t')
        {
            return fallbackAdvanceX * 4f;
        }

        if (font == null)
        {
            return fallbackAdvanceX;
        }

        if (font.TryGetGlyph(currentChar, out var glyph))
        {
            return glyph.AdvanceX * scale;
        }

        if (font.TryGetGlyph(' ', out var spaceGlyph))
        {
            return spaceGlyph.AdvanceX * scale;
        }

        return 0f;
    }

    private static void BuildSegmentStyles(List<RichSpan> spans, int lineStart, int lineEnd, int uniqueBoundaryCount)
    {
        EnsureSegmentStyleCapacity(uniqueBoundaryCount);
        int styleDeltaLength = uniqueBoundaryCount * StyleBitCount;
        Array.Clear(_segmentStyleDeltaScratch, 0, styleDeltaLength);

        for (int spanIndex = 0; spanIndex < spans.Count; spanIndex++)
        {
            var span = spans[spanIndex];
            int clippedStart = Math.Max(span.Start, lineStart);
            int clippedEnd = Math.Min(span.End, lineEnd);
            if (clippedStart >= clippedEnd)
            {
                continue;
            }

            int startBoundaryIndex = Array.BinarySearch(_segmentBoundariesScratch, 0, uniqueBoundaryCount, clippedStart);
            int endBoundaryIndex = Array.BinarySearch(_segmentBoundariesScratch, 0, uniqueBoundaryCount, clippedEnd);
            if (startBoundaryIndex < 0 || endBoundaryIndex < 0)
            {
                continue;
            }

            for (int styleBitIndex = 0; styleBitIndex < StyleBitCount; styleBitIndex++)
            {
                RichSpanStyle styleBit = StyleBits[styleBitIndex];
                if ((span.Style & styleBit) == 0)
                {
                    continue;
                }

                _segmentStyleDeltaScratch[startBoundaryIndex * StyleBitCount + styleBitIndex]++;
                _segmentStyleDeltaScratch[endBoundaryIndex * StyleBitCount + styleBitIndex]--;
            }
        }

        Span<int> activeCounts = stackalloc int[StyleBitCount];
        for (int segmentIndex = 0; segmentIndex < uniqueBoundaryCount - 1; segmentIndex++)
        {
            int deltaBaseIndex = segmentIndex * StyleBitCount;
            RichSpanStyle combinedStyle = RichSpanStyle.None;

            for (int styleBitIndex = 0; styleBitIndex < StyleBitCount; styleBitIndex++)
            {
                activeCounts[styleBitIndex] += _segmentStyleDeltaScratch[deltaBaseIndex + styleBitIndex];
                if (activeCounts[styleBitIndex] > 0)
                {
                    combinedStyle |= StyleBits[styleBitIndex];
                }
            }

            _segmentStylesScratch[segmentIndex] = combinedStyle;
        }
    }

    private static void EnsureSegmentBoundaryCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= _segmentBoundariesScratch.Length)
        {
            return;
        }

        int newCapacity = _segmentBoundariesScratch.Length;
        while (newCapacity < requiredCapacity)
        {
            newCapacity *= 2;
        }

        Array.Resize(ref _segmentBoundariesScratch, newCapacity);
    }

    private static void EnsureSegmentStyleCapacity(int requiredBoundaryCapacity)
    {
        if (requiredBoundaryCapacity > _segmentStylesScratch.Length)
        {
            int newBoundaryCapacity = _segmentStylesScratch.Length;
            while (newBoundaryCapacity < requiredBoundaryCapacity)
            {
                newBoundaryCapacity *= 2;
            }

            Array.Resize(ref _segmentStylesScratch, newBoundaryCapacity);
        }

        int requiredDeltaCapacity = requiredBoundaryCapacity * StyleBitCount;
        if (requiredDeltaCapacity > _segmentStyleDeltaScratch.Length)
        {
            int newDeltaCapacity = _segmentStyleDeltaScratch.Length;
            while (newDeltaCapacity < requiredDeltaCapacity)
            {
                newDeltaCapacity *= 2;
            }

            Array.Resize(ref _segmentStyleDeltaScratch, newDeltaCapacity);
        }
    }
}
