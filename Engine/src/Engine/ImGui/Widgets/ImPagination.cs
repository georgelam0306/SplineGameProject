using DerpLib.ImGui.Core;

namespace DerpLib.ImGui.Widgets;

/// <summary>
/// Pagination widget for navigating through pages of content.
/// Shows page numbers with prev/next buttons.
/// </summary>
/// <example>
/// int totalPages = (totalItems + itemsPerPage - 1) / itemsPerPage;
/// if (ImPagination.Draw("pager", ref currentPage, totalPages, x, y))
/// {
///     // Page changed, reload data
///     LoadPage(currentPage);
/// }
/// </example>
public static class ImPagination
{
    // Visual settings
    public static float ButtonSize = 28f;
    public static float ButtonSpacing = 4f;
    public static float Height = 28f;
    public static int MaxVisiblePages = 7;

    /// <summary>
    /// Draw pagination controls. Returns true if page changed.
    /// </summary>
    /// <param name="id">Unique identifier</param>
    /// <param name="currentPage">Current page (0-based)</param>
    /// <param name="totalPages">Total number of pages</param>
    /// <param name="x">X position</param>
    /// <param name="y">Y position</param>
    public static bool Draw(string id, ref int currentPage, int totalPages, float x, float y)
    {
        if (totalPages <= 0) return false;

        var baseRect = new ImRect(x, y, 0, Height);

        bool changed = false;
        float currentX = baseRect.X;

        // Clamp current page
        currentPage = Math.Clamp(currentPage, 0, totalPages - 1);

        // First button
        if (DrawButton(currentX, baseRect.Y, "<<", currentPage > 0))
        {
            currentPage = 0;
            changed = true;
        }
        currentX += ButtonSize + ButtonSpacing;

        // Previous button
        if (DrawButton(currentX, baseRect.Y, "<", currentPage > 0))
        {
            currentPage--;
            changed = true;
        }
        currentX += ButtonSize + ButtonSpacing;

        // Page numbers
        var (rangeStart, rangeEnd) = CalculatePageRange(currentPage, totalPages);

        // Ellipsis before
        if (rangeStart > 0)
        {
            if (DrawPageButton(currentX, baseRect.Y, 0, currentPage))
            {
                currentPage = 0;
                changed = true;
            }
            currentX += ButtonSize + ButtonSpacing;

            if (rangeStart > 1)
            {
                DrawEllipsis(currentX, baseRect.Y);
                currentX += ButtonSize + ButtonSpacing;
            }
        }

        // Page buttons
        for (int i = rangeStart; i <= rangeEnd; i++)
        {
            if (DrawPageButton(currentX, baseRect.Y, i, currentPage))
            {
                currentPage = i;
                changed = true;
            }
            currentX += ButtonSize + ButtonSpacing;
        }

        // Ellipsis after
        if (rangeEnd < totalPages - 1)
        {
            if (rangeEnd < totalPages - 2)
            {
                DrawEllipsis(currentX, baseRect.Y);
                currentX += ButtonSize + ButtonSpacing;
            }

            if (DrawPageButton(currentX, baseRect.Y, totalPages - 1, currentPage))
            {
                currentPage = totalPages - 1;
                changed = true;
            }
            currentX += ButtonSize + ButtonSpacing;
        }

        // Next button
        if (DrawButton(currentX, baseRect.Y, ">", currentPage < totalPages - 1))
        {
            currentPage++;
            changed = true;
        }
        currentX += ButtonSize + ButtonSpacing;

        // Last button
        if (DrawButton(currentX, baseRect.Y, ">>", currentPage < totalPages - 1))
        {
            currentPage = totalPages - 1;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Draw compact pagination (just prev/next with page info).
    /// </summary>
    public static bool DrawCompact(string id, ref int currentPage, int totalPages, float x, float y)
    {
        if (totalPages <= 0) return false;

        var baseRect = new ImRect(x, y, 0, Height);

        bool changed = false;
        float currentX = baseRect.X;

        currentPage = Math.Clamp(currentPage, 0, totalPages - 1);

        // Previous button
        if (DrawButton(currentX, baseRect.Y, "<", currentPage > 0))
        {
            currentPage--;
            changed = true;
        }
        currentX += ButtonSize + ButtonSpacing;

        // Page info "X / Y"
        Span<char> pageInfo = stackalloc char[32];
        int len = 0;
        (currentPage + 1).TryFormat(pageInfo.Slice(len), out int written);
        len += written;
        pageInfo[len++] = ' ';
        pageInfo[len++] = '/';
        pageInfo[len++] = ' ';
        totalPages.TryFormat(pageInfo.Slice(len), out written);
        len += written;

        var infoSpan = pageInfo.Slice(0, len);
        float textWidth = ImTextMetrics.MeasureWidth(Im.Context.Font, infoSpan, Im.Style.FontSize);
        float infoWidth = textWidth + Im.Style.Padding * 2;

        Im.DrawRoundedRect(currentX, baseRect.Y, infoWidth, Height, Im.Style.CornerRadius, Im.Style.Surface);

        float textX = currentX + (infoWidth - textWidth) / 2;
        float textY = baseRect.Y + (Height - Im.Style.FontSize) / 2;
        Im.Text(infoSpan, textX, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        currentX += infoWidth + ButtonSpacing;

        // Next button
        if (DrawButton(currentX, baseRect.Y, ">", currentPage < totalPages - 1))
        {
            currentPage++;
            changed = true;
        }

        return changed;
    }

    private static (int start, int end) CalculatePageRange(int currentPage, int totalPages)
    {
        if (totalPages <= MaxVisiblePages)
        {
            return (0, totalPages - 1);
        }

        int halfVisible = MaxVisiblePages / 2;
        int start = currentPage - halfVisible;
        int end = currentPage + halfVisible;

        if (start < 0)
        {
            end -= start;
            start = 0;
        }

        if (end >= totalPages)
        {
            start -= (end - totalPages + 1);
            end = totalPages - 1;
        }

        start = Math.Max(0, start);
        end = Math.Min(totalPages - 1, end);

        return (start, end);
    }

    private static bool DrawButton(float x, float y, string text, bool enabled)
    {
        var rect = new ImRect(x, y, ButtonSize, Height);
        bool hovered = enabled && rect.Contains(Im.MousePos);
        bool clicked = false;

        uint bgColor = enabled ? (hovered ? Im.Style.Hover : Im.Style.Surface) : Im.Style.Surface;
        uint textColor = enabled ? Im.Style.TextPrimary : Im.Style.TextDisabled;

        Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, bgColor);
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, Im.Style.Border, 1f);

        // Center text
        float textWidth = ImTextMetrics.MeasureWidth(Im.Context.Font, text.AsSpan(), Im.Style.FontSize);
        float textX = rect.X + (rect.Width - textWidth) / 2;
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) / 2;
        Im.Text(text.AsSpan(), textX, textY, Im.Style.FontSize, textColor);

        if (hovered && Im.MousePressed)
        {
            clicked = true;
        }

        return clicked;
    }

    private static bool DrawPageButton(float x, float y, int pageIndex, int currentPage)
    {
        bool isSelected = pageIndex == currentPage;

        Span<char> text = stackalloc char[8];
        (pageIndex + 1).TryFormat(text, out int textLen);
        var textSpan = text.Slice(0, textLen);

        float textWidth = ImTextMetrics.MeasureWidth(Im.Context.Font, textSpan, Im.Style.FontSize);
        float btnWidth = Math.Max(ButtonSize, textWidth + Im.Style.Padding * 2);

        var rect = new ImRect(x, y, btnWidth, Height);
        bool hovered = rect.Contains(Im.MousePos);

        uint bgColor = isSelected ? Im.Style.Primary : (hovered ? Im.Style.Hover : Im.Style.Surface);
        uint textColor = isSelected ? 0xFFFFFFFF : Im.Style.TextPrimary;

        Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, bgColor);

        float textX = rect.X + (rect.Width - textWidth) / 2;
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) / 2;
        Im.Text(textSpan, textX, textY, Im.Style.FontSize, textColor);

        return hovered && Im.MousePressed && !isSelected;
    }

    private static void DrawEllipsis(float x, float y)
    {
        var rect = new ImRect(x, y, ButtonSize, Height);

        float textWidth = ImTextMetrics.MeasureWidth(Im.Context.Font, "...".AsSpan(), Im.Style.FontSize);
        float textX = rect.X + (rect.Width - textWidth) / 2;
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) / 2;
        Im.Text("...".AsSpan(), textX, textY, Im.Style.FontSize, Im.Style.TextSecondary);
    }
}
