using System.Numerics;
using DerpLib.ImGui.Core;

namespace DerpLib.ImGui.Widgets;

/// <summary>
/// Menu bar widget with dropdown menus.
/// Uses Begin/End pattern for menu hierarchy.
/// </summary>
/// <example>
/// if (ImMenuBar.Begin("main_menu", x, y, width))
/// {
///     if (ImMenuBar.BeginMenu("File"))
///     {
///         if (ImMenuBar.MenuItem("New", "Ctrl+N")) { /* handle */ }
///         if (ImMenuBar.MenuItem("Open", "Ctrl+O")) { /* handle */ }
///         ImMenuBar.Separator();
///         if (ImMenuBar.MenuItem("Exit")) { /* handle */ }
///         ImMenuBar.EndMenu();
///     }
///     ImMenuBar.End();
/// }
/// </example>
public static class ImMenuBar
{
    // State tracking
    private static int _openMenuId;
    private static int _hoveredMenuId;
    private static float _menuBarStartX;
    private static float _menuBarX;
    private static float _menuBarY;
    private static float _menuBarHeight;
    private static float _menuBarRight;
    private static ImRect _currentMenuRect;
    private static bool _inMenuBar;
    private static bool _inMenu;
    private static float _menuItemY;

    // Visual settings
    public static float MenuBarHeight = 28f;
    public static float MenuItemHeight = 26f;
    public static float MenuMinWidth = 150f;
    public static float ShortcutPadding = 40f;

    /// <summary>
    /// Begin a menu bar at the specified position.
    /// </summary>
    public static bool Begin(string id, float x, float y, float width)
    {
        _menuBarStartX = x;
        _menuBarX = x;
        _menuBarY = y;
        _menuBarHeight = MenuBarHeight;
        _menuBarRight = x + width;
        _inMenuBar = true;
        _hoveredMenuId = 0;

        // Draw menu bar background
        Im.DrawRect(x, y, width, MenuBarHeight, Im.Style.Surface);
        Im.DrawLine(x, y + MenuBarHeight, x + width, y + MenuBarHeight, 1f, Im.Style.Border);

        return true;
    }

    /// <summary>
    /// End the menu bar.
    /// </summary>
    public static void End()
    {
        // Close menus if clicked outside
        if (Im.MousePressed && _openMenuId != 0 && _hoveredMenuId == 0)
        {
            bool clickedInMenu = _currentMenuRect.Contains(Im.MousePos);
            bool clickedInBar = new ImRect(_menuBarStartX, _menuBarY, _menuBarRight - _menuBarStartX, _menuBarHeight)
                .Contains(Im.MousePos);

            if (!clickedInMenu && !clickedInBar)
            {
                _openMenuId = 0;
            }
        }

        _inMenuBar = false;
    }

    /// <summary>
    /// Begin a top-level menu. Returns true if the menu is open.
    /// </summary>
    public static bool BeginMenu(string label)
    {
        if (!_inMenuBar) return false;

        int menuId = Im.Context.GetId(label);

        // Measure label
        float textWidth = MeasureTextWidth(label);
        float menuWidth = textWidth + Im.Style.Padding * 4;

        var menuRect = new ImRect(_menuBarX, _menuBarY, menuWidth, _menuBarHeight);
        bool hovered = menuRect.Contains(Im.MousePos);
        bool isOpen = _openMenuId == menuId;

        // Handle interaction
        if (hovered)
        {
            _hoveredMenuId = menuId;

            // Open on click, or if another menu is open (hover to switch)
            if (Im.MousePressed || (_openMenuId != 0 && _openMenuId != menuId))
            {
                _openMenuId = menuId;
                isOpen = true;
            }
        }

        // Draw menu header
        uint bgColor = isOpen ? Im.Style.Active : (hovered ? Im.Style.Hover : 0);
        if (bgColor != 0)
            Im.DrawRect(menuRect.X, menuRect.Y, menuRect.Width, menuRect.Height, bgColor);

        float textX = menuRect.X + (menuRect.Width - textWidth) / 2;
        float textY = menuRect.Y + (menuRect.Height - Im.Style.FontSize) / 2;
        Im.Text(label.AsSpan(), textX, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        // Advance position
        _menuBarX += menuWidth;

        if (isOpen)
        {
            _inMenu = true;
            _menuItemY = _menuBarY + _menuBarHeight + 2;

            // Calculate dropdown dimensions
            _currentMenuRect = new ImRect(
                menuRect.X,
                _menuBarY + _menuBarHeight + 2,
                MenuMinWidth,
                0
            );
        }

        return isOpen;
    }

    /// <summary>
    /// End the current menu.
    /// </summary>
    public static void EndMenu()
    {
        if (!_inMenu) return;

        // Draw menu background (now that we know the height)
        float menuHeight = _menuItemY - _currentMenuRect.Y;
        if (menuHeight > 0)
        {
            _currentMenuRect = new ImRect(_currentMenuRect.X, _currentMenuRect.Y, _currentMenuRect.Width, menuHeight);

            // Draw border
            Im.DrawRoundedRectStroke(_currentMenuRect.X, _currentMenuRect.Y, _currentMenuRect.Width, _currentMenuRect.Height,
                Im.Style.CornerRadius, Im.Style.Border, 1f);
        }

        _inMenu = false;
    }

    /// <summary>
    /// Draw a menu item. Returns true if clicked.
    /// </summary>
    public static bool MenuItem(string label, string shortcut = "")
    {
        if (!_inMenu) return false;

        // Measure for width calculation
        float labelWidth = MeasureTextWidth(label);
        float neededWidth = labelWidth + Im.Style.Padding * 2;
        if (!string.IsNullOrEmpty(shortcut))
        {
            float shortcutWidth = MeasureTextWidth(shortcut);
            neededWidth += ShortcutPadding + shortcutWidth;
        }

        // Expand menu width if needed
        if (neededWidth > _currentMenuRect.Width)
        {
            _currentMenuRect = new ImRect(_currentMenuRect.X, _currentMenuRect.Y, neededWidth, _currentMenuRect.Height);
        }

        var itemRect = new ImRect(_currentMenuRect.X, _menuItemY, _currentMenuRect.Width, MenuItemHeight);
        bool hovered = itemRect.Contains(Im.MousePos);
        bool clicked = false;

        // Draw background
        Im.DrawRect(itemRect.X, itemRect.Y, itemRect.Width, itemRect.Height, Im.Style.Background);
        if (hovered)
        {
            Im.DrawRect(itemRect.X, itemRect.Y, itemRect.Width, itemRect.Height, Im.Style.Hover);
            if (Im.MousePressed)
            {
                clicked = true;
                _openMenuId = 0;
            }
        }

        // Draw label
        float textY = itemRect.Y + (itemRect.Height - Im.Style.FontSize) / 2;
        Im.Text(label.AsSpan(), itemRect.X + Im.Style.Padding, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        // Draw shortcut
        if (!string.IsNullOrEmpty(shortcut))
        {
            float shortcutWidth = MeasureTextWidth(shortcut);
            float shortcutX = itemRect.Right - Im.Style.Padding - shortcutWidth;
            Im.Text(shortcut.AsSpan(), shortcutX, textY, Im.Style.FontSize, Im.Style.TextSecondary);
        }

        _menuItemY += MenuItemHeight;
        return clicked;
    }

    /// <summary>
    /// Draw a disabled menu item.
    /// </summary>
    public static void MenuItemDisabled(string label, string shortcut = "")
    {
        if (!_inMenu) return;

        var itemRect = new ImRect(_currentMenuRect.X, _menuItemY, _currentMenuRect.Width, MenuItemHeight);

        // Draw background
        Im.DrawRect(itemRect.X, itemRect.Y, itemRect.Width, itemRect.Height, Im.Style.Background);

        // Draw label (grayed out)
        float textY = itemRect.Y + (itemRect.Height - Im.Style.FontSize) / 2;
        Im.Text(label.AsSpan(), itemRect.X + Im.Style.Padding, textY, Im.Style.FontSize, Im.Style.TextDisabled);

        // Draw shortcut
        if (!string.IsNullOrEmpty(shortcut))
        {
            float shortcutWidth = MeasureTextWidth(shortcut);
            float shortcutX = itemRect.Right - Im.Style.Padding - shortcutWidth;
            Im.Text(shortcut.AsSpan(), shortcutX, textY, Im.Style.FontSize, Im.Style.TextDisabled);
        }

        _menuItemY += MenuItemHeight;
    }

    /// <summary>
    /// Draw a separator line in the menu.
    /// </summary>
    public static void Separator()
    {
        if (!_inMenu) return;

        float separatorHeight = 9f;
        var itemRect = new ImRect(_currentMenuRect.X, _menuItemY, _currentMenuRect.Width, separatorHeight);

        Im.DrawRect(itemRect.X, itemRect.Y, itemRect.Width, itemRect.Height, Im.Style.Background);
        float lineY = _menuItemY + separatorHeight / 2;
        Im.DrawLine(itemRect.X + 8, lineY, itemRect.Right - 8, lineY, 1f, Im.Style.Border);

        _menuItemY += separatorHeight;
    }

    /// <summary>
    /// Close all open menus.
    /// </summary>
    public static void CloseAll()
    {
        _openMenuId = 0;
    }

    private static float MeasureTextWidth(string text)
    {
        return ImTextMetrics.MeasureWidth(Im.Context.Font, text.AsSpan(), Im.Style.FontSize);
    }
}
