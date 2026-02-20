using System;
using System.Numerics;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Rendering;
using DerpLib.ImGui.Viewport;

namespace DerpLib.ImGui.Widgets;

/// <summary>
/// Context menu widget (viewport overlay) with hover submenus.
/// Uses Begin/End for root menus and BeginMenu/EndMenu for nested menus.
/// </summary>
public static class ImContextMenu
{
    private const int MaxMenuDepth = 8;
    private const float ViewportMargin = 6f;
    private const float SubMenuGap = 4f;
    private const float SubMenuArrowPad = 18f;

    private struct MenuLevel
    {
        public int MenuId;
        public Vector2 Position;
        public float Width;
        public float ItemY;
        public ImRect Rect;
    }

    // Open state (persistent between frames while menu is open)
    private static int _openMenuId;
    private static Vector2 _rootMenuPosition;
    private static float _rootMenuHeightHint;
    private static int _openFrame;
    private static int _openViewportResourcesId;
    private static readonly int[] _openPathIds = new int[MaxMenuDepth];
    private static int _openPathDepth = 1;

    // Per-frame render state
    private static readonly int[] _nextOpenPathIds = new int[MaxMenuDepth];
    private static int _nextOpenPathDepth;
    private static readonly MenuLevel[] _levels = new MenuLevel[MaxMenuDepth];
    private static int _levelDepth;
    private static readonly ImRect[] _drawnRects = new ImRect[MaxMenuDepth];
    private static int _drawnRectCount;

    // Overlay state
    private static bool _inRoot;
    private static ImDrawLayer _previousLayer;
    private static int _previousSortKey;
    private static Vector4 _previousClipRect;
    private static bool _pushedCancelTransform;
    private static bool _overlayActive;
    private static bool _pushedClipOverride;

    // Visual settings
    public static float ItemHeight = 26f;
    public static float MinWidth = 150f;
    public static float ShortcutPadding = 40f;

    public static void Open(string id)
    {
        _openMenuId = Im.Context.GetId(id);
        _openFrame = Im.Context.FrameCount;
        _openViewportResourcesId = Im.CurrentViewport?.ResourcesId ?? 0;
        _rootMenuPosition = Im.MousePosViewport;
        _rootMenuHeightHint = 0f;
        _openPathDepth = 1;
        _openPathIds[0] = _openMenuId;

        // If the menu is opened while the left mouse button is already held, reserve that click
        // so the newly opened menu cannot "click through" on the same press.
        // This mirrors Dear ImGui's popup behavior (no interaction on the opening click).
        var ctx = Im.Context;
        if (ctx.Input.MouseDown && ctx.MouseDownOwnerLeft == 0)
        {
            ctx.MouseDownOwnerLeft = _openMenuId;
        }
        if (ctx.Input.MouseRightDown && ctx.MouseDownOwnerRight == 0)
        {
            ctx.MouseDownOwnerRight = _openMenuId;
        }
        if (ctx.Input.MouseMiddleDown && ctx.MouseDownOwnerMiddle == 0)
        {
            ctx.MouseDownOwnerMiddle = _openMenuId;
        }
    }

    public static void OpenAt(string id, float x, float y)
    {
        _openMenuId = Im.Context.GetId(id);
        _openFrame = Im.Context.FrameCount;
        _openViewportResourcesId = Im.CurrentViewport?.ResourcesId ?? 0;
        _rootMenuPosition = Im.TransformPointLocalToViewport(new Vector2(x, y));
        _rootMenuHeightHint = 0f;
        _openPathDepth = 1;
        _openPathIds[0] = _openMenuId;

        var ctx = Im.Context;
        if (ctx.Input.MouseDown && ctx.MouseDownOwnerLeft == 0)
        {
            ctx.MouseDownOwnerLeft = _openMenuId;
        }
        if (ctx.Input.MouseRightDown && ctx.MouseDownOwnerRight == 0)
        {
            ctx.MouseDownOwnerRight = _openMenuId;
        }
        if (ctx.Input.MouseMiddleDown && ctx.MouseDownOwnerMiddle == 0)
        {
            ctx.MouseDownOwnerMiddle = _openMenuId;
        }
    }

    public static bool IsOpen(string id)
    {
        return _openMenuId == Im.Context.GetId(id);
    }

    /// <summary>
    /// Prime overlay capture before normal UI runs, so background widgets cannot consume input while a context menu is open.
    /// </summary>
    public static void PrimeOverlayCapture(ImContext ctx)
    {
        if (_openMenuId == 0)
        {
            return;
        }

        var currentViewport = ctx.CurrentViewport;
        if (currentViewport == null)
        {
            return;
        }

        if (_openViewportResourcesId != 0 && currentViewport.ResourcesId != _openViewportResourcesId)
        {
            return;
        }

        ctx.AddOverlayCaptureRect(new ImRect(0f, 0f, currentViewport.Size.X, currentViewport.Size.Y));
    }

    public static bool Begin(string id)
    {
        int menuId = Im.Context.GetId(id);
        if (_openMenuId != menuId)
        {
            return false;
        }

        var viewport = Im.CurrentViewport;
        if (viewport == null)
        {
            _openMenuId = 0;
            _openViewportResourcesId = 0;
            return false;
        }

        BeginOverlay(viewport);

        _drawnRectCount = 0;
        _levelDepth = 0;
        _inRoot = true;

        _nextOpenPathDepth = 1;
        _nextOpenPathIds[0] = _openMenuId;
        for (int i = 1; i < MaxMenuDepth; i++)
        {
            _nextOpenPathIds[i] = 0;
        }

        float heightHint = GetRootMenuHeightHint(viewport, _rootMenuPosition);
        Vector2 rootPos = ClampMenuPosition(viewport, _rootMenuPosition, MinWidth, heightHint);
        ImPopover.AddCaptureRect(new ImRect(rootPos.X, rootPos.Y, MinWidth, heightHint));
        _levels[0] = new MenuLevel
        {
            MenuId = menuId,
            Position = rootPos,
            Width = MinWidth,
            ItemY = rootPos.Y,
            Rect = new ImRect(rootPos.X, rootPos.Y, MinWidth, 0f),
        };

        return true;
    }

    public static void End()
    {
        if (!_inRoot)
        {
            return;
        }

        if (_levelDepth != 0)
        {
            throw new InvalidOperationException("ImContextMenu.End called while a submenu is still open. Missing EndMenu()?");
        }

        FinalizeAndDrawCurrentMenu(borderOnly: true);

        // Use the real height for clamping in subsequent frames (prevents large upward offsets near the bottom).
        var viewport = Im.CurrentViewport;
        if (viewport != null)
        {
            _rootMenuHeightHint = _levels[0].Rect.Height;
            _rootMenuPosition = ClampMenuPosition(viewport, _rootMenuPosition, _levels[0].Width, _rootMenuHeightHint);
        }

        if (ImPopover.ShouldClose(
                openedFrame: _openFrame,
                closeOnEscape: true,
                closeOnOutsideButtons: ImPopoverCloseButtons.Left | ImPopoverCloseButtons.Right | ImPopoverCloseButtons.Middle,
                consumeCloseClick: false,
                requireNoMouseOwner: true,
                useViewportMouseCoordinates: true,
                insideRects: _drawnRects,
                insideRectCount: _drawnRectCount))
        {
            _openMenuId = 0;
            _openViewportResourcesId = 0;
        }

        // Update submenu chain (hover-based).
        if (_openMenuId != 0)
        {
            _openPathDepth = _nextOpenPathDepth;
            for (int i = 0; i < _openPathDepth; i++)
            {
                _openPathIds[i] = _nextOpenPathIds[i];
            }
            for (int i = _openPathDepth; i < MaxMenuDepth; i++)
            {
                _openPathIds[i] = 0;
            }
        }
        else
        {
            _openPathDepth = 1;
            for (int i = 0; i < MaxMenuDepth; i++)
            {
                _openPathIds[i] = 0;
            }
        }

        _inRoot = false;
        CleanupOverlay();
    }

    public static bool Item(string label, string shortcut = "")
    {
        if (!_inRoot)
        {
            return false;
        }

        ref var level = ref _levels[_levelDepth];
        int itemId = GetMenuItemId(level.MenuId, label);

        float neededWidth = MeasureItemWidth(label, shortcut);
        EnsureMenuWidth(ref level, neededWidth);

        var itemRect = new ImRect(level.Position.X, level.ItemY, level.Width, ItemHeight);
        bool hovered = itemRect.Contains(Im.MousePosViewport);

        bool clicked = false;
        var ctx = Im.Context;

        // Draw background
        Im.DrawRect(itemRect.X, itemRect.Y, itemRect.Width, itemRect.Height, Im.Style.Surface);
        if (hovered)
        {
            Im.DrawRect(itemRect.X, itemRect.Y, itemRect.Width, itemRect.Height, Im.Style.Hover);
        }

        // Interaction (active-on-press, commit on release)
        if (hovered)
        {
            ctx.SetHot(itemId);
        }

        if (hovered && Im.MousePressed)
        {
            ctx.SetActive(itemId);
            if (ctx.IsActive(itemId))
            {
                ctx.ConsumeMouseLeftPress();
            }
        }

        if (ctx.IsActive(itemId) && ctx.Input.MouseReleased)
        {
            if (hovered)
            {
                clicked = true;
                _openMenuId = 0;
            }
            ctx.ConsumeMouseLeftRelease();
            ctx.ClearActive();
        }

        // Draw label
        float textY = itemRect.Y + (itemRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), itemRect.X + Im.Style.Padding, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        // Draw shortcut
        if (!string.IsNullOrEmpty(shortcut))
        {
            float shortcutWidth = MeasureTextWidth(shortcut);
            float shortcutX = itemRect.Right - Im.Style.Padding - shortcutWidth;
            Im.Text(shortcut.AsSpan(), shortcutX, textY, Im.Style.FontSize, Im.Style.TextSecondary);
        }

        level.ItemY += ItemHeight;
        return clicked;
    }

    public static void ItemDisabled(string label, string shortcut = "")
    {
        if (!_inRoot)
        {
            return;
        }

        ref var level = ref _levels[_levelDepth];
        float neededWidth = MeasureItemWidth(label, shortcut);
        EnsureMenuWidth(ref level, neededWidth);

        var itemRect = new ImRect(level.Position.X, level.ItemY, level.Width, ItemHeight);
        Im.DrawRect(itemRect.X, itemRect.Y, itemRect.Width, itemRect.Height, Im.Style.Surface);

        float textY = itemRect.Y + (itemRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), itemRect.X + Im.Style.Padding, textY, Im.Style.FontSize, Im.Style.TextDisabled);

        if (!string.IsNullOrEmpty(shortcut))
        {
            float shortcutWidth = MeasureTextWidth(shortcut);
            float shortcutX = itemRect.Right - Im.Style.Padding - shortcutWidth;
            Im.Text(shortcut.AsSpan(), shortcutX, textY, Im.Style.FontSize, Im.Style.TextDisabled);
        }

        level.ItemY += ItemHeight;
    }

    public static bool ItemCheckbox(string label, ref bool isChecked, string shortcut = "")
    {
        if (!_inRoot)
        {
            return false;
        }

        ref var level = ref _levels[_levelDepth];
        int itemId = GetMenuItemId(level.MenuId, label);

        float neededWidth = MeasureItemWidth(label, shortcut);
        EnsureMenuWidth(ref level, neededWidth);

        var itemRect = new ImRect(level.Position.X, level.ItemY, level.Width, ItemHeight);
        bool hovered = itemRect.Contains(Im.MousePosViewport);

        bool changed = false;
        var ctx = Im.Context;

        // Draw background
        Im.DrawRect(itemRect.X, itemRect.Y, itemRect.Width, itemRect.Height, Im.Style.Surface);
        if (hovered)
        {
            Im.DrawRect(itemRect.X, itemRect.Y, itemRect.Width, itemRect.Height, Im.Style.Hover);
        }

        if (hovered)
        {
            ctx.SetHot(itemId);
        }

        if (hovered && Im.MousePressed)
        {
            ctx.SetActive(itemId);
            if (ctx.IsActive(itemId))
            {
                ctx.ConsumeMouseLeftPress();
            }
        }

        if (ctx.IsActive(itemId) && ctx.Input.MouseReleased)
        {
            if (hovered)
            {
                isChecked = !isChecked;
                changed = true;
            }
            ctx.ConsumeMouseLeftRelease();
            ctx.ClearActive();
        }

        float checkX = itemRect.X + Im.Style.Padding;
        float checkY = itemRect.Center.Y;
        float textX = checkX + 20f;
        if (isChecked)
        {
            Im.DrawLine(checkX + 2, checkY, checkX + 6, checkY + 4, 2f, Im.Style.Primary);
            Im.DrawLine(checkX + 6, checkY + 4, checkX + 14, checkY - 4, 2f, Im.Style.Primary);
        }

        float textY = itemRect.Y + (itemRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), textX, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        if (!string.IsNullOrEmpty(shortcut))
        {
            float shortcutWidth = MeasureTextWidth(shortcut);
            float shortcutX = itemRect.Right - Im.Style.Padding - shortcutWidth;
            Im.Text(shortcut.AsSpan(), shortcutX, textY, Im.Style.FontSize, Im.Style.TextSecondary);
        }

        level.ItemY += ItemHeight;
        return changed;
    }

    /// <summary>
    /// Begin a hover-open submenu. Returns true if open; call EndMenu() when finished.
    /// </summary>
    public static bool BeginMenu(string label)
    {
        if (!_inRoot)
        {
            return false;
        }

        if (_levelDepth >= MaxMenuDepth - 1)
        {
            return false;
        }

        ref var parent = ref _levels[_levelDepth];
        int submenuId = GetSubMenuId(parent.MenuId, label);

        float neededWidth = MeasureItemWidth(label, shortcut: string.Empty) + SubMenuArrowPad;
        EnsureMenuWidth(ref parent, neededWidth);

        var itemRect = new ImRect(parent.Position.X, parent.ItemY, parent.Width, ItemHeight);
        bool hovered = itemRect.Contains(Im.MousePosViewport);

        // Draw background
        Im.DrawRect(itemRect.X, itemRect.Y, itemRect.Width, itemRect.Height, Im.Style.Surface);
        if (hovered)
        {
            Im.DrawRect(itemRect.X, itemRect.Y, itemRect.Width, itemRect.Height, Im.Style.Hover);
        }

        // Draw label
        float textY = itemRect.Y + (itemRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), itemRect.X + Im.Style.Padding, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        // Draw chevron
        float chevronSize = 12f;
        float chevronX = itemRect.Right - Im.Style.Padding - chevronSize;
        float chevronY = itemRect.Y + (itemRect.Height - chevronSize) * 0.5f;
        ImIcons.DrawChevron(chevronX, chevronY, chevronSize, ImIcons.ChevronDirection.Right, Im.Style.TextSecondary);

        parent.ItemY += ItemHeight;

        // Determine if submenu should remain open.
        bool alreadyOpen = _openPathDepth > _levelDepth + 1 && _openPathIds[_levelDepth + 1] == submenuId;
        var viewport = Im.CurrentViewport;
        if (viewport == null)
        {
            return false;
        }

        float subHeightEstimate = EstimateMenuHeight(viewport);
        ImRect subRectEstimate = PlaceSubMenu(viewport, parent.Position, parent.Width, itemRect.Y, MinWidth, subHeightEstimate);
        ImPopover.AddCaptureRect(subRectEstimate);
        bool mouseInSubRect = alreadyOpen && subRectEstimate.Contains(Im.MousePosViewport);
        bool openThisFrame = hovered || mouseInSubRect;

        if (!openThisFrame)
        {
            float bridgeY0 = itemRect.Y - 2f;
            float bridgeY1 = itemRect.Bottom + 2f;
            float x0 = MathF.Min(itemRect.Right, subRectEstimate.X);
            float x1 = MathF.Max(itemRect.Right, subRectEstimate.X);
            var bridge = new ImRect(x0, bridgeY0, MathF.Max(0f, x1 - x0), MathF.Max(0f, bridgeY1 - bridgeY0));
            if (bridge.Width > 0f && bridge.Height > 0f && bridge.Contains(Im.MousePosViewport))
            {
                openThisFrame = true;
            }
        }

        if (!openThisFrame)
        {
            return false;
        }

        _nextOpenPathDepth = Math.Max(_nextOpenPathDepth, _levelDepth + 2);
        _nextOpenPathIds[_levelDepth + 1] = submenuId;

        _levelDepth++;
        _levels[_levelDepth] = new MenuLevel
        {
            MenuId = submenuId,
            Position = new Vector2(subRectEstimate.X, subRectEstimate.Y),
            Width = MinWidth,
            ItemY = subRectEstimate.Y,
            Rect = new ImRect(subRectEstimate.X, subRectEstimate.Y, MinWidth, 0f),
        };

        return true;
    }

    public static void EndMenu()
    {
        if (!_inRoot || _levelDepth <= 0)
        {
            return;
        }

        FinalizeAndDrawCurrentMenu(borderOnly: true);
        _levelDepth--;
    }

    public static void Separator()
    {
        if (!_inRoot)
        {
            return;
        }

        ref var level = ref _levels[_levelDepth];
        float separatorHeight = 9f;
        var itemRect = new ImRect(level.Position.X, level.ItemY, level.Width, separatorHeight);

        Im.DrawRect(itemRect.X, itemRect.Y, itemRect.Width, itemRect.Height, Im.Style.Surface);
        float lineY = level.ItemY + separatorHeight * 0.5f;
        Im.DrawLine(itemRect.X + 8, lineY, itemRect.Right - 8, lineY, 1f, Im.Style.Border);

        level.ItemY += separatorHeight;
    }

    public static void Close()
    {
        _openMenuId = 0;
        _openViewportResourcesId = 0;
    }

    private static void BeginOverlay(ImViewport viewport)
    {
        _overlayActive = false;
        _pushedCancelTransform = false;

        _previousLayer = viewport.CurrentLayer;
        var drawList = viewport.GetDrawList(ImDrawLayer.Overlay);
        _previousSortKey = drawList.GetSortKey();
        _previousClipRect = drawList.GetClipRect();
        Im.SetDrawLayer(ImDrawLayer.Overlay);
        drawList.SetSortKey(int.MaxValue - 256);
        drawList.ClearClipRect();
        _overlayActive = true;
        _pushedClipOverride = false;

        if (Im.CurrentTransformMatrix != Matrix3x2.Identity)
        {
            Im.PushInverseTransform();
            _pushedCancelTransform = true;
        }

        ImPopover.EnterOverlayScope();

        Im.PushClipRectOverride(new ImRect(0f, 0f, viewport.Size.X, viewport.Size.Y));
        _pushedClipOverride = true;
    }

    private static void CleanupOverlay()
    {
        if (!_overlayActive)
        {
            return;
        }

        if (_pushedClipOverride)
        {
            Im.PopClipRect();
            _pushedClipOverride = false;
        }

        if (_pushedCancelTransform)
        {
            Im.PopTransform();
            _pushedCancelTransform = false;
        }

        ImPopover.ExitOverlayScope();

        var viewport = Im.CurrentViewport;
        if (viewport != null)
        {
            var drawList = viewport.GetDrawList(ImDrawLayer.Overlay);
            drawList.SetClipRect(_previousClipRect);
            drawList.SetSortKey(_previousSortKey);
            Im.SetDrawLayer(_previousLayer);
        }

        _overlayActive = false;
    }

    private static float EstimateMenuHeight(ImViewport viewport)
    {
        const float estimatedMenuItems = 12f;
        float estimated = ItemHeight * estimatedMenuItems;
        float max = viewport.Size.Y - ViewportMargin * 2f;
        return MathF.Min(estimated, max);
    }

    private static float GetRootMenuHeightHint(ImViewport viewport, Vector2 rootPos)
    {
        // Prefer using the last known height for the currently open menu, so it clamps correctly.
        float hint = _rootMenuHeightHint;
        if (hint <= 0f || !float.IsFinite(hint))
        {
            // Keep this conservative so menus opened near the bottom don't get pushed far upward on the opening frame.
            hint = ItemHeight * 6f;
        }

        float max = viewport.Size.Y - ViewportMargin * 2f;
        if (hint > max)
        {
            hint = max;
        }

        // Clamp to the remaining space below the requested position so the opening frame stays near the cursor.
        float availableBelow = viewport.Size.Y - ViewportMargin - rootPos.Y;
        if (availableBelow < hint)
        {
            hint = MathF.Max(ItemHeight, availableBelow);
        }

        return hint;
    }

    private static Vector2 ClampMenuPosition(ImViewport viewport, Vector2 pos, float width, float height)
    {
        float x = pos.X;
        float y = pos.Y;

        if (x + width > viewport.Size.X - ViewportMargin)
        {
            x = viewport.Size.X - ViewportMargin - width;
        }
        if (x < ViewportMargin)
        {
            x = ViewportMargin;
        }
        if (y + height > viewport.Size.Y - ViewportMargin)
        {
            y = viewport.Size.Y - ViewportMargin - height;
        }
        if (y < ViewportMargin)
        {
            y = ViewportMargin;
        }

        return new Vector2(x, y);
    }

    private static ImRect PlaceSubMenu(ImViewport viewport, Vector2 parentPos, float parentWidth, float itemY, float width, float estimatedHeight)
    {
        float x = parentPos.X + parentWidth + SubMenuGap;
        float y = itemY;

        bool openLeft = x + width > viewport.Size.X - ViewportMargin;
        if (openLeft)
        {
            x = parentPos.X - width - SubMenuGap;
        }

        var pos = ClampMenuPosition(viewport, new Vector2(x, y), width, estimatedHeight);
        return new ImRect(pos.X, pos.Y, width, estimatedHeight);
    }

    private static void EnsureMenuWidth(ref MenuLevel level, float neededWidth)
    {
        if (neededWidth <= level.Width)
        {
            return;
        }

        level.Width = neededWidth;
        level.Rect = new ImRect(level.Rect.X, level.Rect.Y, neededWidth, level.Rect.Height);
    }

    private static float MeasureItemWidth(string label, string shortcut)
    {
        float labelWidth = MeasureTextWidth(label);
        float neededWidth = labelWidth + Im.Style.Padding * 2f;
        if (!string.IsNullOrEmpty(shortcut))
        {
            float shortcutWidth = MeasureTextWidth(shortcut);
            neededWidth += ShortcutPadding + shortcutWidth;
        }
        return MathF.Max(MinWidth, neededWidth);
    }

    private static float MeasureTextWidth(string text)
    {
        return ImTextMetrics.MeasureWidth(Im.Context.Font, text.AsSpan(), Im.Style.FontSize);
    }

    private static int GetMenuItemId(int menuId, string label)
    {
        var ctx = Im.Context;
        ctx.PushId(menuId);
        int id = ctx.GetId(label);
        ctx.PopId();
        return id;
    }

    private static int GetSubMenuId(int menuId, string label)
    {
        // Separate id namespace from normal items to avoid collisions.
        var ctx = Im.Context;
        ctx.PushId(menuId);
        int id = ctx.GetId(label + "##submenu");
        ctx.PopId();
        return id;
    }

    private static void FinalizeAndDrawCurrentMenu(bool borderOnly)
    {
        ref var level = ref _levels[_levelDepth];

        float menuHeight = level.ItemY - level.Position.Y;
        level.Rect = new ImRect(level.Position.X, level.Position.Y, level.Width, menuHeight);
        ImPopover.AddCaptureRect(level.Rect);

        if (_drawnRectCount < _drawnRects.Length)
        {
            _drawnRects[_drawnRectCount++] = level.Rect;
        }

        // Always draw a full background behind items to avoid 1px gaps from float rounding and/or
        // late width expansion (which can leave uncovered strips between item fills).
        // Render it behind existing item commands via a lower sort key.
        {
            ImViewport? viewport = Im.CurrentViewport;
            if (viewport != null)
            {
                var drawList = viewport.GetDrawList(ImDrawLayer.Overlay);
                int previousSortKey = drawList.GetSortKey();
                int backgroundSortKey = previousSortKey == int.MinValue ? int.MinValue : previousSortKey - 1;
                drawList.SetSortKey(backgroundSortKey);
                Im.DrawRoundedRect(level.Rect.X, level.Rect.Y, level.Rect.Width, level.Rect.Height, Im.Style.CornerRadius, Im.Style.Surface);
                drawList.SetSortKey(previousSortKey);
            }
        }

        Im.DrawRoundedRectStroke(level.Rect.X, level.Rect.Y, level.Rect.Width, level.Rect.Height, Im.Style.CornerRadius, Im.Style.Border, Im.Style.BorderWidth);
    }
}
