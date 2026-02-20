using System.Numerics;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Windows;

namespace DerpLib.ImGui.Docking;

/// <summary>
/// A docking layout that wraps a dock tree.
/// Used for both the main viewport layout and floating window layouts.
/// </summary>
public class DockingLayout
{
    private static int _nextId = 1;

    /// <summary>Unique layout ID.</summary>
    public int Id { get; }

    /// <summary>Root node of the dock tree (leaf or split).</summary>
    public ImDockNode? Root;

    /// <summary>Layout bounds in viewport coordinates.</summary>
    public ImRect Rect;

    /// <summary>True if this is the main viewport layout.</summary>
    public bool IsMainLayout;

    public DockingLayout()
    {
        Id = _nextId++;
    }

    /// <summary>
    /// Returns true if this layout represents a floating dock group (multiple windows).
    /// Single-window floating layouts are treated as normal windows (window owns its rect + chrome).
    /// </summary>
    public bool IsFloatingGroup()
    {
        if (IsMainLayout)
        {
            return false;
        }

        // Zero allocation: early-exit traversal.
        return HasMultipleWindows();
    }

    /// <summary>
    /// Chrome height reserved above the dock area for floating groups.
    /// </summary>
    public float GetChromeHeight(ImStyle style)
    {
        if (!IsFloatingGroup())
        {
            return 0f;
        }

        return style.TitleBarHeight;
    }

    /// <summary>
    /// Dock rect inside the outer layout rect (below chrome).
    /// All dock nodes are laid out within this rect.
    /// </summary>
    public ImRect GetDockRect(ImStyle style)
    {
        float chromeHeight = GetChromeHeight(style);
        float height = Rect.Height - chromeHeight;
        if (height < 0f)
        {
            height = 0f;
        }

        return new ImRect(Rect.X, Rect.Y + chromeHeight, Rect.Width, height);
    }

    /// <summary>
    /// Get the total number of windows in this layout (zero allocation).
    /// </summary>
    public int GetWindowCount()
    {
        if (Root == null) return 0;
        return CountWindowsRecursive(Root);
    }

    private static int CountWindowsRecursive(ImDockNode node)
    {
        if (node is ImDockLeaf leaf)
        {
            return leaf.WindowIds.Count;
        }
        else if (node is ImDockSplit split)
        {
            int count = 0;
            if (split.First != null)
                count += CountWindowsRecursive(split.First);
            if (split.Second != null)
                count += CountWindowsRecursive(split.Second);
            return count;
        }
        return 0;
    }

    /// <summary>
    /// Check if this layout has more than one window (zero allocation, early exit).
    /// </summary>
    public bool HasMultipleWindows()
    {
        if (Root == null) return false;
        int count = 0;
        return HasMultipleWindowsRecursive(Root, ref count);
    }

    private static bool HasMultipleWindowsRecursive(ImDockNode node, ref int count)
    {
        if (node is ImDockLeaf leaf)
        {
            count += leaf.WindowIds.Count;
            return count > 1;
        }
        else if (node is ImDockSplit split)
        {
            if (split.First != null && HasMultipleWindowsRecursive(split.First, ref count))
                return true;
            if (split.Second != null && HasMultipleWindowsRecursive(split.Second, ref count))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Get the first window ID in this layout, or -1 if empty (zero allocation).
    /// </summary>
    public int GetFirstWindowId()
    {
        if (Root == null) return -1;
        return GetFirstWindowIdRecursive(Root);
    }

    private static int GetFirstWindowIdRecursive(ImDockNode node)
    {
        if (node is ImDockLeaf leaf)
        {
            return leaf.WindowIds.Count > 0 ? leaf.WindowIds[0] : -1;
        }
        else if (node is ImDockSplit split)
        {
            if (split.First != null)
            {
                int id = GetFirstWindowIdRecursive(split.First);
                if (id >= 0) return id;
            }
            if (split.Second != null)
            {
                return GetFirstWindowIdRecursive(split.Second);
            }
        }
        return -1;
    }

    /// <summary>
    /// Check if any window in this layout is open (zero allocation).
    /// </summary>
    public bool HasAnyOpenWindow(ImWindowManager windowManager)
    {
        if (Root == null) return false;
        return HasAnyOpenWindowRecursive(Root, windowManager);
    }

    private static bool HasAnyOpenWindowRecursive(ImDockNode node, ImWindowManager windowManager)
    {
        if (node is ImDockLeaf leaf)
        {
            for (int i = 0; i < leaf.WindowIds.Count; i++)
            {
                var window = windowManager.FindWindowById(leaf.WindowIds[i]);
                if (window != null && window.IsOpen)
                    return true;
            }
            return false;
        }
        else if (node is ImDockSplit split)
        {
            if (split.First != null && HasAnyOpenWindowRecursive(split.First, windowManager))
                return true;
            if (split.Second != null && HasAnyOpenWindowRecursive(split.Second, windowManager))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Find the leaf containing a specific window.
    /// </summary>
    public ImDockLeaf? FindLeafWithWindow(int windowId)
    {
        return Root?.FindLeafWithWindow(windowId);
    }

    /// <summary>
    /// Check if this layout contains a specific window.
    /// </summary>
    public bool ContainsWindow(int windowId)
    {
        return FindLeafWithWindow(windowId) != null;
    }

    /// <summary>
    /// Update layout for all nodes in the tree.
    /// </summary>
    public void UpdateLayout()
    {
        // Main layout uses full rect; floating groups reserve chrome at the top.
        var style = Im.Style;
        var dockRect = GetDockRect(style);
        Root?.UpdateLayout(dockRect);
    }

    /// <summary>
    /// Draw the dock tree (backgrounds, tab bars, splitters).
    /// </summary>
    public void Draw()
    {
        Root?.Draw();
    }

    /// <summary>
    /// Draw the frame for a floating layout (background, border, title/tab bar).
    /// Called from BeginWindowCore instead of DrawWindowFrame.
    /// </summary>
    /// <param name="window">The window being rendered (for title, z-order, etc.)</param>
    /// <param name="localRect">Window rect in local coordinates (0,0 based)</param>
    /// <param name="style">Current style</param>
    /// <param name="windowManager">Window manager for looking up other windows in tabs</param>
    public void DrawFloatingFrame(ImWindow window, ImRect localRect, ImStyle style, ImWindowManager windowManager)
    {
        if (IsMainLayout) return; // Main layout has no frame

        // Drawing in local coordinates - transform stack already positions us
        float x = localRect.X;
        float y = localRect.Y;
        float w = localRect.Width;
        float h = window.IsCollapsed ? style.TitleBarHeight : localRect.Height;

        // Window background with shadow
        var shadowColor = ImStyle.ToVector4(style.ShadowColor);
        var bgColor = ImStyle.ToVector4(style.Background);

        Im.AddRoundedRectWithShadowLocal(
            x, y, w, h,
            style.CornerRadius,
            bgColor,
            style.ShadowOffsetX,
            style.ShadowOffsetY,
            style.ShadowRadius,
            shadowColor);

        // Check window count (zero allocation)
        bool hasMultiple = HasMultipleWindows();

        if (!hasMultiple)
        {
            // Single window: draw title bar (like a normal window)
            DrawTitleBar(window, localRect, style);
        }
        else
        {
            // Multiple windows: draw tab bar
            DrawTabBar(localRect, style, windowManager);
        }

        // Border
        Im.DrawRoundedRectStroke(x, y, w, h, style.CornerRadius, style.Border, style.BorderWidth);
    }

    /// <summary>
    /// Draw a title bar for single-window layouts (matches DrawWindowFrame behavior).
    /// </summary>
    private void DrawTitleBar(ImWindow window, ImRect localRect, ImStyle style)
    {
        float x = localRect.X;
        float y = localRect.Y;
        float w = localRect.Width;

        bool isFocused = Im.WindowManager.FocusedWindow == window;
        uint titleBarColor = isFocused ? style.TitleBar : style.TitleBarInactive;

        Im.DrawRoundedRect(x, y, w, style.TitleBarHeight, style.CornerRadius, titleBarColor);

        // Title bar bottom edge (to connect with body when not collapsed)
        if (!window.IsCollapsed)
        {
            Im.DrawRect(x, y + style.TitleBarHeight - style.CornerRadius,
                w, style.CornerRadius, titleBarColor);
        }

        // Title text
        float textX = x + style.Padding;
        float textY = y + (style.TitleBarHeight - 14) / 2;
        Im.LabelTextViewport(window.Title, textX, textY);

        // Close button (if not NoClose)
        if (!window.Flags.HasFlag(ImWindowFlags.NoClose))
        {
            float btnSize = style.TitleBarHeight - 8;
            float btnX = x + w - btnSize - 4;
            float btnY = y + 4;

            if (Im.ButtonInternal(window.Id ^ 0x636C6F73, btnX, btnY, btnSize, btnSize)) // "clos"
            {
                window.IsOpen = false;
            }

            // Draw X
            float pad = btnSize * 0.3f;
            Im.DrawLine(btnX + pad, btnY + pad, btnX + btnSize - pad, btnY + btnSize - pad, 2f, style.TextPrimary);
            Im.DrawLine(btnX + btnSize - pad, btnY + pad, btnX + pad, btnY + btnSize - pad, 2f, style.TextPrimary);
        }

        // Collapse button (if not NoCollapse)
        if (!window.Flags.HasFlag(ImWindowFlags.NoCollapse))
        {
            float btnSize = style.TitleBarHeight - 8;
            float offset = window.Flags.HasFlag(ImWindowFlags.NoClose) ? 4 : btnSize + 8;
            float btnX = x + w - btnSize - offset;
            float btnY = y + 4;

            if (Im.ButtonInternal(window.Id ^ 0x636F6C6C, btnX, btnY, btnSize, btnSize)) // "coll"
            {
                window.IsCollapsed = !window.IsCollapsed;
            }

            // Draw collapse indicator
            float pad = btnSize * 0.3f;
            if (window.IsCollapsed)
            {
                Im.DrawLine(btnX + pad, btnY + pad, btnX + btnSize - pad, btnY + btnSize / 2, 2f, style.TextPrimary);
                Im.DrawLine(btnX + btnSize - pad, btnY + btnSize / 2, btnX + pad, btnY + btnSize - pad, 2f, style.TextPrimary);
            }
            else
            {
                Im.DrawLine(btnX + pad, btnY + btnSize / 2, btnX + btnSize - pad, btnY + btnSize / 2, 2f, style.TextPrimary);
            }
        }
    }

    /// <summary>
    /// Draw a tab bar for multi-window layouts.
    /// </summary>
    private void DrawTabBar(ImRect localRect, ImStyle style, ImWindowManager windowManager)
    {
        if (Root is not ImDockLeaf leaf) return;

        float x = localRect.X;
        float y = localRect.Y;
        float tabBarHeight = style.TabHeight;

        // Tab bar background
        Im.DrawRoundedRect(x, y, localRect.Width, tabBarHeight, style.CornerRadius, style.TabInactive);

        // Draw tabs
        float tabX = x;
        float tabWidth = Math.Min(150f, localRect.Width / Math.Max(1, leaf.WindowIds.Count));
        var input = Im.Context.Input;

        for (int i = 0; i < leaf.WindowIds.Count; i++)
        {
            var tabWindow = windowManager.FindWindowById(leaf.WindowIds[i]);
            if (tabWindow == null) continue;

            var tabRect = new ImRect(tabX, y, tabWidth, tabBarHeight);
            bool isActive = i == leaf.ActiveTabIndex;
            bool hovered = tabRect.Contains(input.MousePos);

            // Handle tab click
            if (hovered && input.MousePressed)
            {
                leaf.ActiveTabIndex = i;
            }

            // Draw tab background
            uint tabColor = isActive ? style.TabActive : (hovered ? style.Hover : style.TabInactive);
            Im.DrawRoundedRect(tabRect.X, tabRect.Y, tabRect.Width, tabRect.Height, style.CornerRadius, tabColor);

            // Draw tab title
            float textX = tabRect.X + style.Padding;
            float textY = tabRect.Y + (tabRect.Height - style.FontSize) * 0.5f;
            float closeSizeForClip = tabBarHeight * 0.5f;
            float closeXForClip = tabRect.Right - closeSizeForClip - 4f;
            float textClipRight = hovered ? (closeXForClip - style.Padding) : (tabRect.Right - style.Padding);
            if (textClipRight < textX)
            {
                textClipRight = textX;
            }
            Im.PushClipRect(new ImRect(textX, tabRect.Y, textClipRight - textX, tabRect.Height));
            Im.LabelText(tabWindow.Title, textX, textY);
            Im.PopClipRect();

            // Close button on hover
            if (hovered)
            {
                float closeSize = tabBarHeight * 0.5f;
                float closeX = tabRect.Right - closeSize - 4;
                float closeY = tabRect.Y + (tabRect.Height - closeSize) * 0.5f;
                var closeRect = new ImRect(closeX, closeY, closeSize, closeSize);

                if (closeRect.Contains(input.MousePos))
                {
                    Im.DrawRoundedRect(closeRect.X, closeRect.Y, closeRect.Width, closeRect.Height, 2, style.Hover);
                    if (input.MousePressed)
                    {
                        tabWindow.IsOpen = false;
                    }
                }

                // Draw X
                float pad = closeSize * 0.3f;
                Im.DrawLine(closeRect.X + pad, closeRect.Y + pad,
                    closeRect.X + closeSize - pad, closeRect.Y + closeSize - pad, 1.5f, style.TextPrimary);
                Im.DrawLine(closeRect.X + closeSize - pad, closeRect.Y + pad,
                    closeRect.X + pad, closeRect.Y + closeSize - pad, 1.5f, style.TextPrimary);
            }

            tabX += tabWidth;
        }
    }

    /// <summary>
    /// Get the content rect for a floating layout (area below title/tab bar).
    /// </summary>
    public ImRect GetContentRect(ImRect localRect, ImStyle style)
    {
        if (IsMainLayout) return localRect;

        // Check if multiple windows (zero allocation)
        float headerHeight = HasMultipleWindows() ? style.TabHeight : style.TitleBarHeight;
        return new ImRect(
            localRect.X,
            localRect.Y + headerHeight,
            localRect.Width,
            localRect.Height - headerHeight);
    }
}
