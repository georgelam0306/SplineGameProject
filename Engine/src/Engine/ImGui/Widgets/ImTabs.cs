using DerpLib.ImGui.Core;

namespace DerpLib.ImGui.Widgets;

/// <summary>
/// Tabbed panel widget for organizing content into switchable views.
/// Uses Begin/End pattern with tab items.
/// </summary>
/// <example>
/// if (ImTabs.Begin("my_tabs", x, y, width, ref selectedTab))
/// {
///     if (ImTabs.BeginTab("General"))
///     {
///         // Draw general tab content
///         ImTabs.EndTab();
///     }
///     if (ImTabs.BeginTab("Settings"))
///     {
///         // Draw settings tab content
///         ImTabs.EndTab();
///     }
///     ImTabs.End();
/// }
/// </example>
public static class ImTabs
{
    // State tracking
    private static float _tabBarX;
    private static float _tabBarY;
    private static float _tabBarWidth;
    private static float _contentY;
    private static int _currentTabIndex;
    private static int _selectedTabIndex;
    private static int _tabCount;
    private static bool _inTabs;
    private static bool _inTab;
    private static bool _tabChanged;
    private static int _hoveredTabIndex;
    private static float _currentTabX; // Accumulated X position for laying out tabs

    // Visual settings
    public static float TabHeight = 32f;
    public static float TabPadding = 16f;
    public static float TabGap = 2f;
    public static float ContentPadding = 8f;

    /// <summary>
    /// Begin a tab container at the specified position.
    /// </summary>
    /// <param name="id">Unique identifier for this tab group.</param>
    /// <param name="x">X position (window-local).</param>
    /// <param name="y">Y position (window-local).</param>
    /// <param name="width">Width of the tab bar.</param>
    /// <param name="selectedIndex">Reference to the currently selected tab index.</param>
    /// <returns>True always (for consistent Begin/End pattern).</returns>
    public static bool Begin(string id, float x, float y, float width, ref int selectedIndex)
    {
        _tabBarX = x;
        _tabBarY = y;
        _tabBarWidth = width;
        _contentY = y + TabHeight;
        _currentTabIndex = 0;
        _selectedTabIndex = selectedIndex;
        _tabCount = 0;
        _inTabs = true;
        _inTab = false;
        _tabChanged = false;
        _hoveredTabIndex = -1;
        _currentTabX = 0; // Reset tab X position for this frame

        var rect = new ImRect(x, y, width, TabHeight);

        // Draw tab bar background
        Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.Surface);
        Im.DrawLine(rect.X, rect.Y + rect.Height, rect.X + rect.Width, rect.Y + rect.Height, 1f, Im.Style.Border);

        return true;
    }

    /// <summary>
    /// Begin a tab. Returns true if this tab is selected and content should be drawn.
    /// </summary>
    /// <param name="label">Tab label text.</param>
    /// <returns>True if this tab is selected.</returns>
    public static bool BeginTab(string label)
    {
        if (!_inTabs) return false;

        int tabIndex = _currentTabIndex;
        _currentTabIndex++;
        _tabCount++;

        // Measure tab
        var textSize = MeasureText(label);
        float tabWidth = textSize.X + TabPadding * 2;

        // Calculate tab position (accumulate X as we go)
        float tabX = _tabBarX + _currentTabX;
        var rect = new ImRect(tabX, _tabBarY, tabWidth, TabHeight);

        // Hit test in screen coordinates
        bool isSelected = tabIndex == _selectedTabIndex;
        bool hovered = rect.Contains(Im.MousePos);

        if (hovered)
        {
            _hoveredTabIndex = tabIndex;
            if (Im.MousePressed)
            {
                _selectedTabIndex = tabIndex;
                _tabChanged = true;
                isSelected = true;
            }
        }

        float radius = Im.Style.CornerRadius;

        // Draw tab background
        if (isSelected)
        {
            Im.DrawRoundedRectPerCorner(rect.X, rect.Y, rect.Width, rect.Height,
                radius, radius, 0, 0, Im.Style.Background);
            Im.DrawLine(rect.X + 2, rect.Y + 2, rect.X + rect.Width - 2, rect.Y + 2, 2f, Im.Style.Primary);
        }
        else if (hovered)
        {
            Im.DrawRoundedRectPerCorner(rect.X, rect.Y, rect.Width, rect.Height,
                radius, radius, 0, 0, Im.Style.Hover);
        }

        // Draw tab label
        uint textColor = isSelected ? Im.Style.TextPrimary : Im.Style.TextSecondary;
        float textX = rect.X + (rect.Width - textSize.X) / 2;
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) / 2;
        Im.Text(label.AsSpan(), textX, textY, Im.Style.FontSize, textColor);

        // Advance X position for next tab
        _currentTabX += tabWidth + TabGap;

        if (isSelected)
        {
            _inTab = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// End the current tab content area.
    /// </summary>
    public static void EndTab()
    {
        _inTab = false;
    }

    /// <summary>
    /// End the tab container. Updates the selected index if changed.
    /// </summary>
    /// <param name="selectedIndex">Reference to update with new selection.</param>
    public static void End(ref int selectedIndex)
    {
        if (_tabChanged)
        {
            selectedIndex = _selectedTabIndex;
        }
        _inTabs = false;
        _inTab = false;
    }

    /// <summary>
    /// End the tab container (without updating selection - use when Begin passed ref).
    /// </summary>
    public static void End()
    {
        _inTabs = false;
        _inTab = false;
    }

    /// <summary>
    /// Get the Y position where tab content should start.
    /// </summary>
    public static float GetContentY() => _contentY + ContentPadding;

    /// <summary>
    /// Get the content area rect for the current tab.
    /// </summary>
    public static ImRect GetContentRect(float height)
    {
        return new ImRect(_tabBarX, _contentY, _tabBarWidth, height);
    }

    /// <summary>
    /// Check if a tab was just selected this frame.
    /// </summary>
    public static bool TabChanged => _tabChanged;

    /// <summary>
    /// Get the currently selected tab index.
    /// </summary>
    public static int SelectedIndex => _selectedTabIndex;

    // Simple tab width tracking (up to 16 tabs)
    private static readonly float[] _tabWidths = new float[16];

    /// <summary>
    /// Measures text width for tab sizing.
    /// </summary>
    private static TextSize MeasureText(string text)
    {
        float width = ImTextMetrics.MeasureWidth(Im.Context.Font, text.AsSpan(), Im.Style.FontSize);
        return new TextSize(width, Im.Style.FontSize);
    }

    private readonly struct TextSize
    {
        public readonly float X;
        public readonly float Y;
        public TextSize(float x, float y) { X = x; Y = y; }
    }

    private static void StoreTabWidth(int index, float width)
    {
        if (index < 16)
            _tabWidths[index] = width;
    }

    private static float GetTabOffset(int tabIndex)
    {
        float offset = 0;
        for (int i = 0; i < tabIndex && i < 16; i++)
        {
            offset += _tabWidths[i] + TabGap;
        }
        return offset;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Alternative API: Simple tabs without Begin/End content
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Draw a simple tab bar and return the selected index.
    /// Use this when you handle content drawing separately.
    /// </summary>
    /// <param name="id">Unique identifier.</param>
    /// <param name="labels">Array of tab labels.</param>
    /// <param name="selectedIndex">Currently selected tab index.</param>
    /// <param name="x">X position.</param>
    /// <param name="y">Y position.</param>
    /// <param name="width">Width of tab bar.</param>
    /// <returns>True if selection changed.</returns>
    public static bool TabBar(string id, ReadOnlySpan<string> labels, ref int selectedIndex, float x, float y, float width)
    {
        bool changed = false;
        float localTabX = 0;

        // Draw tab bar background
        var barRect = new ImRect(x, y, width, TabHeight);
        Im.DrawRect(barRect.X, barRect.Y, barRect.Width, barRect.Height, Im.Style.Surface);
        Im.DrawLine(barRect.X, barRect.Y + barRect.Height, barRect.X + barRect.Width, barRect.Y + barRect.Height, 1f, Im.Style.Border);

        float radius = Im.Style.CornerRadius;

        for (int i = 0; i < labels.Length; i++)
        {
            string label = labels[i];
            var textSize = MeasureText(label);
            float tabWidth = textSize.X + TabPadding * 2;

            var rect = new ImRect(x + localTabX, y, tabWidth, TabHeight);
            bool isSelected = i == selectedIndex;
            bool hovered = rect.Contains(Im.MousePos);

            if (hovered && Im.MousePressed && !isSelected)
            {
                selectedIndex = i;
                changed = true;
                isSelected = true;
            }

            // Draw tab
            if (isSelected)
            {
                Im.DrawRoundedRectPerCorner(rect.X, rect.Y, rect.Width, rect.Height,
                    radius, radius, 0, 0, Im.Style.Background);
                Im.DrawLine(rect.X + 2, rect.Y + 2, rect.X + rect.Width - 2, rect.Y + 2, 2f, Im.Style.Primary);
            }
            else if (hovered)
            {
                Im.DrawRoundedRectPerCorner(rect.X, rect.Y, rect.Width, rect.Height,
                    radius, radius, 0, 0, Im.Style.Hover);
            }

            uint textColor = isSelected ? Im.Style.TextPrimary : Im.Style.TextSecondary;
            float textX = rect.X + (rect.Width - textSize.X) / 2;
            float textY = rect.Y + (rect.Height - Im.Style.FontSize) / 2;
            Im.Text(label.AsSpan(), textX, textY, Im.Style.FontSize, textColor);

            localTabX += tabWidth + TabGap;
        }

        return changed;
    }

    /// <summary>
    /// Draw a closeable tab bar with X buttons on each tab.
    /// </summary>
    /// <param name="id">Unique identifier.</param>
    /// <param name="labels">Array of tab labels.</param>
    /// <param name="selectedIndex">Currently selected tab index.</param>
    /// <param name="x">X position.</param>
    /// <param name="y">Y position.</param>
    /// <param name="width">Width of tab bar.</param>
    /// <param name="closedTabIndex">Set to the index of a closed tab, or -1 if none closed.</param>
    /// <returns>True if selection changed.</returns>
    public static bool CloseableTabBar(string id, ReadOnlySpan<string> labels, ref int selectedIndex,
        float x, float y, float width, out int closedTabIndex)
    {
        bool changed = false;
        closedTabIndex = -1;
        float localTabX = 0;
        float closeButtonSize = 16f;
        float closeButtonPadding = 4f;

        // Draw tab bar background
        var barRect = new ImRect(x, y, width, TabHeight);
        Im.DrawRect(barRect.X, barRect.Y, barRect.Width, barRect.Height, Im.Style.Surface);
        Im.DrawLine(barRect.X, barRect.Y + barRect.Height, barRect.X + barRect.Width, barRect.Y + barRect.Height, 1f, Im.Style.Border);

        float radius = Im.Style.CornerRadius;

        for (int i = 0; i < labels.Length; i++)
        {
            string label = labels[i];
            var textSize = MeasureText(label);
            float tabWidth = textSize.X + TabPadding * 2 + closeButtonSize + closeButtonPadding;

            var rect = new ImRect(x + localTabX, y, tabWidth, TabHeight);
            bool isSelected = i == selectedIndex;
            bool hovered = rect.Contains(Im.MousePos);

            // Close button rect
            float closeX = rect.X + tabWidth - closeButtonSize - closeButtonPadding;
            float closeY = rect.Y + (TabHeight - closeButtonSize) / 2;
            var closeRect = new ImRect(closeX, closeY, closeButtonSize, closeButtonSize);
            bool closeHovered = closeRect.Contains(Im.MousePos);

            // Handle close button click
            if (closeHovered && Im.MousePressed)
            {
                closedTabIndex = i;
            }
            // Handle tab selection (but not if clicking close button)
            else if (hovered && Im.MousePressed && !isSelected)
            {
                selectedIndex = i;
                changed = true;
                isSelected = true;
            }

            // Draw tab
            if (isSelected)
            {
                Im.DrawRoundedRectPerCorner(rect.X, rect.Y, rect.Width, rect.Height,
                    radius, radius, 0, 0, Im.Style.Background);
                Im.DrawLine(rect.X + 2, rect.Y + 2, rect.X + rect.Width - 2, rect.Y + 2, 2f, Im.Style.Primary);
            }
            else if (hovered)
            {
                Im.DrawRoundedRectPerCorner(rect.X, rect.Y, rect.Width, rect.Height,
                    radius, radius, 0, 0, Im.Style.Hover);
            }

            // Draw label (offset to make room for close button)
            float labelWidth = textSize.X + TabPadding * 2;
            uint textColor = isSelected ? Im.Style.TextPrimary : Im.Style.TextSecondary;
            float textX = rect.X + (labelWidth - textSize.X) / 2;
            float textY = rect.Y + (rect.Height - Im.Style.FontSize) / 2;
            Im.Text(label.AsSpan(), textX, textY, Im.Style.FontSize, textColor);

            // Draw close button
            if (hovered || isSelected)
            {
                uint closeColor = closeHovered ? 0xFFFF4444 : Im.Style.TextSecondary;
                if (closeHovered)
                {
                    Im.DrawCircle(closeX + closeButtonSize / 2, closeY + closeButtonSize / 2,
                        closeButtonSize / 2, Im.Style.Hover);
                }
                // Draw X
                float cx = closeX + closeButtonSize / 2;
                float cy = closeY + closeButtonSize / 2;
                float xSize = 4f;
                Im.DrawLine(cx - xSize, cy - xSize, cx + xSize, cy + xSize, 1.5f, closeColor);
                Im.DrawLine(cx + xSize, cy - xSize, cx - xSize, cy + xSize, 1.5f, closeColor);
            }

            localTabX += tabWidth + TabGap;
        }

        return changed;
    }
}
