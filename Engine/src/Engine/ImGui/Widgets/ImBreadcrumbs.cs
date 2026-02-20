using DerpLib.ImGui.Core;

namespace DerpLib.ImGui.Widgets;

/// <summary>
/// Breadcrumbs navigation widget showing hierarchical path.
/// Each crumb is clickable to navigate back to that level.
/// </summary>
/// <example>
/// string[] path = { "Home", "Documents", "Projects", "MyProject" };
/// int clicked = ImBreadcrumbs.Draw("nav", path, x, y);
/// if (clicked >= 0)
/// {
///     // Navigate to path[0..clicked]
///     currentPath = path.Take(clicked + 1).ToArray();
/// }
/// </example>
public static class ImBreadcrumbs
{
    // Visual settings
    public static float ItemPadding = 8f;
    public static float SeparatorWidth = 16f;
    public static float Height = 28f;
    public static uint SeparatorColor = 0xFF888888;

    /// <summary>
    /// Draw breadcrumbs from a path array. Returns index of clicked item, or -1 if none.
    /// </summary>
    public static int Draw(string id, ReadOnlySpan<string> path, float x, float y)
    {
        if (path.Length == 0) return -1;

        var baseRect = new ImRect(x, y, 0, Height);

        int clickedIndex = -1;
        float currentX = baseRect.X;

        for (int i = 0; i < path.Length; i++)
        {
            string crumb = path[i];
            bool isLast = i == path.Length - 1;

            // Measure text
            float textWidth = ImTextMetrics.MeasureWidth(Im.Context.Font, crumb.AsSpan(), Im.Style.FontSize);
            float crumbWidth = textWidth + ItemPadding * 2;

            var crumbRect = new ImRect(currentX, baseRect.Y, crumbWidth, Height);
            bool hovered = crumbRect.Contains(Im.MousePos);

            // Draw crumb
            if (!isLast)
            {
                // Clickable crumb
                uint textColor = hovered ? Im.Style.Primary : Im.Style.TextSecondary;
                if (hovered)
                {
                    Im.DrawRect(crumbRect.X, crumbRect.Y, crumbRect.Width, crumbRect.Height, Im.Style.Hover);
                    if (Im.MousePressed)
                    {
                        clickedIndex = i;
                    }
                }
                // Center text
                float textX = crumbRect.X + ItemPadding;
                float textY = crumbRect.Y + (crumbRect.Height - Im.Style.FontSize) / 2;
                Im.Text(crumb.AsSpan(), textX, textY, Im.Style.FontSize, textColor);
            }
            else
            {
                // Last crumb (current location) - not clickable
                float textX = crumbRect.X + ItemPadding;
                float textY = crumbRect.Y + (crumbRect.Height - Im.Style.FontSize) / 2;
                Im.Text(crumb.AsSpan(), textX, textY, Im.Style.FontSize, Im.Style.TextPrimary);
            }

            currentX += crumbWidth;

            // Draw separator (except after last item)
            if (!isLast)
            {
                float sepX = currentX + SeparatorWidth / 2;
                float sepY = baseRect.Y + Height / 2;
                DrawSeparator(sepX, sepY);
                currentX += SeparatorWidth;
            }
        }

        return clickedIndex;
    }

    /// <summary>
    /// Draw breadcrumbs from a path string with a separator.
    /// </summary>
    public static int Draw(string id, string path, char separator, float x, float y)
    {
        if (string.IsNullOrEmpty(path)) return -1;

        var baseRect = new ImRect(x, y, 0, Height);

        // Split path - use stackalloc for small paths
        Span<Range> ranges = stackalloc Range[32];
        int count = 0;
        int start = 0;

        for (int i = 0; i <= path.Length; i++)
        {
            if (i == path.Length || path[i] == separator)
            {
                if (i > start && count < ranges.Length)
                {
                    ranges[count++] = new Range(start, i);
                }
                start = i + 1;
            }
        }

        if (count == 0) return -1;

        // Draw using ranges
        int clickedIndex = -1;
        float currentX = baseRect.X;

        for (int i = 0; i < count; i++)
        {
            var range = ranges[i];
            var crumb = path.AsSpan(range);
            bool isLast = i == count - 1;

            // Measure text
            float textWidth = ImTextMetrics.MeasureWidth(Im.Context.Font, crumb, Im.Style.FontSize);
            float crumbWidth = textWidth + ItemPadding * 2;

            var crumbRect = new ImRect(currentX, baseRect.Y, crumbWidth, Height);
            bool hovered = crumbRect.Contains(Im.MousePos);

            // Draw crumb
            if (!isLast)
            {
                uint textColor = hovered ? Im.Style.Primary : Im.Style.TextSecondary;
                if (hovered)
                {
                    Im.DrawRect(crumbRect.X, crumbRect.Y, crumbRect.Width, crumbRect.Height, Im.Style.Hover);
                    if (Im.MousePressed)
                    {
                        clickedIndex = i;
                    }
                }
                float textY = crumbRect.Y + (crumbRect.Height - Im.Style.FontSize) / 2;
                Im.Text(crumb, crumbRect.X + ItemPadding, textY, Im.Style.FontSize, textColor);
            }
            else
            {
                float textY = crumbRect.Y + (crumbRect.Height - Im.Style.FontSize) / 2;
                Im.Text(crumb, crumbRect.X + ItemPadding, textY, Im.Style.FontSize, Im.Style.TextPrimary);
            }

            currentX += crumbWidth;

            // Draw separator
            if (!isLast)
            {
                float sepX = currentX + SeparatorWidth / 2;
                float sepY = baseRect.Y + Height / 2;
                DrawSeparator(sepX, sepY);
                currentX += SeparatorWidth;
            }
        }

        return clickedIndex;
    }

    /// <summary>
    /// Draw breadcrumbs with a home icon as the first item.
    /// Returns -2 for home click, or index of path segment clicked.
    /// </summary>
    public static int DrawWithHome(string id, ReadOnlySpan<string> path, float x, float y)
    {
        var baseRect = new ImRect(x, y, 0, Height);

        int clickedIndex = -1;
        float currentX = baseRect.X;

        // Draw home icon
        float homeSize = Height;
        var homeRect = new ImRect(currentX, baseRect.Y, homeSize, Height);
        bool homeHovered = homeRect.Contains(Im.MousePos);

        if (homeHovered)
        {
            Im.DrawRect(homeRect.X, homeRect.Y, homeRect.Width, homeRect.Height, Im.Style.Hover);
            if (Im.MousePressed)
            {
                clickedIndex = -2; // Special value for home
            }
        }

        // Draw home icon (simple house shape)
        DrawHomeIcon(homeRect.X + homeRect.Width / 2, homeRect.Y + homeRect.Height / 2, 8f,
            homeHovered ? Im.Style.Primary : Im.Style.TextSecondary);

        currentX += homeSize;

        // Draw separator after home
        if (path.Length > 0)
        {
            float sepX = currentX + SeparatorWidth / 2;
            float sepY = baseRect.Y + Height / 2;
            DrawSeparator(sepX, sepY);
            currentX += SeparatorWidth;
        }

        // Draw remaining path
        for (int i = 0; i < path.Length; i++)
        {
            string crumb = path[i];
            bool isLast = i == path.Length - 1;

            float textWidth = ImTextMetrics.MeasureWidth(Im.Context.Font, crumb.AsSpan(), Im.Style.FontSize);
            float crumbWidth = textWidth + ItemPadding * 2;

            var crumbRect = new ImRect(currentX, baseRect.Y, crumbWidth, Height);
            bool hovered = crumbRect.Contains(Im.MousePos);

            if (!isLast)
            {
                uint textColor = hovered ? Im.Style.Primary : Im.Style.TextSecondary;
                if (hovered)
                {
                    Im.DrawRect(crumbRect.X, crumbRect.Y, crumbRect.Width, crumbRect.Height, Im.Style.Hover);
                    if (Im.MousePressed)
                    {
                        clickedIndex = i;
                    }
                }
                float textX = crumbRect.X + ItemPadding;
                float textY = crumbRect.Y + (crumbRect.Height - Im.Style.FontSize) / 2;
                Im.Text(crumb.AsSpan(), textX, textY, Im.Style.FontSize, textColor);
            }
            else
            {
                float textX = crumbRect.X + ItemPadding;
                float textY = crumbRect.Y + (crumbRect.Height - Im.Style.FontSize) / 2;
                Im.Text(crumb.AsSpan(), textX, textY, Im.Style.FontSize, Im.Style.TextPrimary);
            }

            currentX += crumbWidth;

            if (!isLast)
            {
                float sepX = currentX + SeparatorWidth / 2;
                float sepY = baseRect.Y + Height / 2;
                DrawSeparator(sepX, sepY);
                currentX += SeparatorWidth;
            }
        }

        return clickedIndex;
    }

    private static void DrawSeparator(float x, float y)
    {
        // Draw chevron (>)
        float size = 4f;
        Im.DrawLine(x - size, y - size, x, y, 1.5f, SeparatorColor);
        Im.DrawLine(x, y, x - size, y + size, 1.5f, SeparatorColor);
    }

    private static void DrawHomeIcon(float cx, float cy, float size, uint color)
    {
        float halfSize = size / 2;
        float roofTop = cy - halfSize;
        float roofBottom = cy - halfSize * 0.3f;
        float houseBottom = cy + halfSize;
        float houseWidth = halfSize * 0.7f;

        // Roof (triangle)
        Im.DrawLine(cx - halfSize, roofBottom, cx, roofTop, 1.5f, color);
        Im.DrawLine(cx, roofTop, cx + halfSize, roofBottom, 1.5f, color);

        // House body (square)
        Im.DrawLine(cx - houseWidth, roofBottom, cx - houseWidth, houseBottom, 1.5f, color);
        Im.DrawLine(cx - houseWidth, houseBottom, cx + houseWidth, houseBottom, 1.5f, color);
        Im.DrawLine(cx + houseWidth, houseBottom, cx + houseWidth, roofBottom, 1.5f, color);
    }
}
