using System.Numerics;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Rendering;
using DerpLib.ImGui.Viewport;

namespace DerpLib.ImGui.Widgets;

/// <summary>
/// Dear ImGui-style main menu bar API, implemented on top of ImContextMenu for overlay dropdowns/submenus.
/// </summary>
public static class ImMainMenuBar
{
    private static bool _inMainMenuBar;
    private static float _barX;
    private static float _barY;
    private static float _barHeight;
    private static float _cursorX;
    private static bool _anyMenuOpen;
    private static int _menuDepth;
    private static bool _pushedId;
    private static bool _overlayActive;
    private static ImDrawLayer _previousLayer;
    private static int _previousSortKey;
    private static Vector4 _previousClipRect;
    private static bool _pushedCancelTransform;
    private static bool _pushedClipOverride;
    private static ImRect _barRect;

    public static float MenuBarHeight = 28f;
    public static float ItemPaddingX = 10f;

    public static bool Begin()
    {
        var viewport = Im.CurrentViewport;
        if (viewport == null)
        {
            return false;
        }

        BeginOverlay(viewport);

        var style = Im.Style;
        var rect = new ImRect(0f, 0f, viewport.Size.X, MenuBarHeight);

        Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, style.Surface);
        Im.DrawLine(rect.X, rect.Bottom, rect.Right, rect.Bottom, 1f, style.Border);

        _inMainMenuBar = true;
        _barX = rect.X;
        _barY = rect.Y;
        _barHeight = rect.Height;
        _barRect = rect;
        _cursorX = rect.X + style.Padding;
        _anyMenuOpen = false;
        _menuDepth = 0;

        Im.Context.PushId(0x4D454E55); // "MENU"
        _pushedId = true;

        return true;
    }

    public static void End()
    {
        if (!_inMainMenuBar)
        {
            return;
        }

        if (_pushedId)
        {
            Im.Context.PopId();
            _pushedId = false;
        }

        _inMainMenuBar = false;
        CleanupOverlay();
    }

    public static bool BeginMenu(string label)
    {
        if (label == null)
        {
            return false;
        }

        if (_inMainMenuBar && _menuDepth == 0)
        {
            float textWidth = ImTextMetrics.MeasureWidth(Im.Context.Font, label.AsSpan(), Im.Style.FontSize);
            float w = textWidth + ItemPaddingX * 2f;
            var rect = new ImRect(_cursorX, _barY, w, _barHeight);

            bool hovered = rect.Contains(Im.MousePos);
            bool open = ImContextMenu.IsOpen(label);
            if (open)
            {
                if (!_anyMenuOpen)
                {
                    ImPopover.AddCaptureRect(_barRect);
                }
                _anyMenuOpen = true;
            }

            if (hovered)
            {
                if (Im.MousePressed || (_anyMenuOpen && !open))
                {
                    ImContextMenu.OpenAt(label, rect.X, rect.Bottom);
                    open = true;
                    _anyMenuOpen = true;
                }
            }

            uint bg = open ? Im.Style.Active : (hovered ? Im.Style.Hover : 0u);
            if (bg != 0u)
            {
                Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, bg);
            }

            float textX = rect.X + ItemPaddingX;
            float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
            Im.Text(label.AsSpan(), textX, textY, Im.Style.FontSize, Im.Style.TextPrimary);

            _cursorX += rect.Width;

            if (!open)
            {
                return false;
            }

            if (!ImContextMenu.Begin(label))
            {
                return false;
            }

            _menuDepth = 1;
            return true;
        }

        if (_menuDepth > 0)
        {
            bool opened = ImContextMenu.BeginMenu(label);
            if (opened)
            {
                _menuDepth++;
            }
            return opened;
        }

        return false;
    }

    public static void EndMenu()
    {
        if (_menuDepth <= 0)
        {
            return;
        }

        if (_menuDepth == 1)
        {
            ImContextMenu.End();
            _menuDepth = 0;
            return;
        }

        ImContextMenu.EndMenu();
        _menuDepth--;
    }

    public static bool MenuItem(string label, string shortcut = "", bool enabled = true)
    {
        if (_menuDepth <= 0)
        {
            return false;
        }

        if (!enabled)
        {
            ImContextMenu.ItemDisabled(label, shortcut);
            return false;
        }

        return ImContextMenu.Item(label, shortcut);
    }

    public static bool MenuItem(string label, string shortcut, ref bool selected, bool enabled = true)
    {
        if (_menuDepth <= 0)
        {
            return false;
        }

        if (!enabled)
        {
            ImContextMenu.ItemDisabled(label, shortcut);
            return false;
        }

        return ImContextMenu.ItemCheckbox(label, ref selected, shortcut);
    }

    public static void Separator()
    {
        if (_menuDepth <= 0)
        {
            return;
        }

        ImContextMenu.Separator();
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
        drawList.SetSortKey(int.MaxValue - 512);
        drawList.ClearClipRect();

        if (Im.CurrentTransformMatrix != Matrix3x2.Identity)
        {
            Im.PushInverseTransform();
            _pushedCancelTransform = true;
        }

        ImPopover.EnterOverlayScope();

        Im.PushClipRectOverride(new ImRect(0f, 0f, viewport.Size.X, viewport.Size.Y));
        _pushedClipOverride = true;

        _overlayActive = true;
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
}
