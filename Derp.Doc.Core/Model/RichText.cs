namespace Derp.Doc.Model;

[Flags]
public enum RichSpanStyle : ushort
{
    None = 0,
    Bold = 1 << 0,
    Italic = 1 << 1,
    Code = 1 << 2,
    Strikethrough = 1 << 3,
    Underline = 1 << 4,
    Highlight = 1 << 5,
}

public struct RichSpan
{
    public int Start;
    public int Length;
    public RichSpanStyle Style;

    public int End => Start + Length;
}

public sealed class RichText
{
    public string PlainText { get; set; } = "";
    public List<RichSpan> Spans { get; set; } = new();

    public RichText Clone()
    {
        var clone = new RichText { PlainText = PlainText };
        foreach (var span in Spans)
            clone.Spans.Add(span);
        return clone;
    }

    /// <summary>
    /// Toggles a style flag on the given character range.
    /// If the entire range already has the style, removes it. Otherwise, adds it.
    /// </summary>
    public void ToggleStyle(int start, int length, RichSpanStyle style)
    {
        if (length <= 0) return;

        int end = start + length;

        // Check if the entire range already has this style
        bool allHaveStyle = HasStyleInRange(start, length, style);

        if (allHaveStyle)
            RemoveStyle(start, length, style);
        else
            AddStyle(start, length, style);
    }

    /// <summary>
    /// Returns true if every character in the range has the given style.
    /// </summary>
    public bool HasStyleInRange(int start, int length, RichSpanStyle style)
    {
        if (length <= 0) return false;
        int end = start + length;

        // Build coverage from spans that have this style
        var covered = new List<(int Start, int End)>();
        foreach (var span in Spans)
        {
            if ((span.Style & style) != 0)
            {
                int overlapStart = Math.Max(span.Start, start);
                int overlapEnd = Math.Min(span.End, end);
                if (overlapStart < overlapEnd)
                    covered.Add((overlapStart, overlapEnd));
            }
        }

        if (covered.Count == 0) return false;

        // Sort and merge intervals
        covered.Sort((a, b) => a.Start.CompareTo(b.Start));
        int mergedStart = covered[0].Start;
        int mergedEnd = covered[0].End;
        for (int i = 1; i < covered.Count; i++)
        {
            if (covered[i].Start <= mergedEnd)
                mergedEnd = Math.Max(mergedEnd, covered[i].End);
            else
                return false; // gap found
        }

        return mergedStart <= start && mergedEnd >= end;
    }

    /// <summary>
    /// Adds a style to a range. Merges/extends existing spans where possible.
    /// </summary>
    public void AddStyle(int start, int length, RichSpanStyle style)
    {
        if (length <= 0) return;
        Spans.Add(new RichSpan { Start = start, Length = length, Style = style });
        NormalizeSpans();
    }

    /// <summary>
    /// Removes a style from a range.
    /// </summary>
    public void RemoveStyle(int start, int length, RichSpanStyle style)
    {
        if (length <= 0) return;
        int end = start + length;

        var newSpans = new List<RichSpan>();
        foreach (var span in Spans)
        {
            if ((span.Style & style) == 0 || span.End <= start || span.Start >= end)
            {
                // No overlap or different style
                newSpans.Add(span);
                continue;
            }

            // This span overlaps and has the target style — split/trim
            var remainingStyle = span.Style & ~style;
            var keptStyle = span.Style;

            // Part before the removal range
            if (span.Start < start)
            {
                newSpans.Add(new RichSpan { Start = span.Start, Length = start - span.Start, Style = keptStyle });
            }

            // Part after the removal range
            if (span.End > end)
            {
                newSpans.Add(new RichSpan { Start = end, Length = span.End - end, Style = keptStyle });
            }

            // The overlapping part, without the removed style
            if (remainingStyle != RichSpanStyle.None)
            {
                int overlapStart = Math.Max(span.Start, start);
                int overlapEnd = Math.Min(span.End, end);
                if (overlapEnd > overlapStart)
                {
                    newSpans.Add(new RichSpan { Start = overlapStart, Length = overlapEnd - overlapStart, Style = remainingStyle });
                }
            }
        }

        Spans.Clear();
        Spans.AddRange(newSpans);
        NormalizeSpans();
    }

    /// <summary>
    /// Adjusts all span offsets after a text insertion at the given position.
    /// </summary>
    public void AdjustForInsert(int position, int insertedLength)
    {
        for (int i = 0; i < Spans.Count; i++)
        {
            var span = Spans[i];
            if (span.Start >= position)
            {
                span.Start += insertedLength;
            }
            else if (span.End > position)
            {
                span.Length += insertedLength;
            }
            Spans[i] = span;
        }
    }

    /// <summary>
    /// Adjusts all span offsets after a text deletion at the given position.
    /// </summary>
    public void AdjustForDelete(int position, int deletedLength)
    {
        int end = position + deletedLength;
        var kept = new List<RichSpan>();

        foreach (var span in Spans)
        {
            var s = span;
            if (s.End <= position)
            {
                // Entirely before deletion — keep as-is
                kept.Add(s);
            }
            else if (s.Start >= end)
            {
                // Entirely after deletion — shift left
                s.Start -= deletedLength;
                kept.Add(s);
            }
            else
            {
                // Overlaps with deletion
                int newStart = Math.Min(s.Start, position);
                int newEnd = Math.Max(s.End - deletedLength, position);
                int newLength = newEnd - newStart;
                if (newLength > 0)
                {
                    s.Start = newStart;
                    s.Length = newLength;
                    kept.Add(s);
                }
            }
        }

        Spans.Clear();
        Spans.AddRange(kept);
    }

    /// <summary>
    /// Gets the effective style at a character position by combining all overlapping spans.
    /// </summary>
    public RichSpanStyle GetStyleAt(int position)
    {
        var result = RichSpanStyle.None;
        foreach (var span in Spans)
        {
            if (position >= span.Start && position < span.End)
                result |= span.Style;
        }
        return result;
    }

    /// <summary>
    /// Merges overlapping spans with the same style.
    /// </summary>
    private void NormalizeSpans()
    {
        if (Spans.Count <= 1) return;

        // Remove zero-length spans
        Spans.RemoveAll(s => s.Length <= 0);
    }
}
