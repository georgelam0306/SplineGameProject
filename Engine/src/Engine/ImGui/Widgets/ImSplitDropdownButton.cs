using System;
using DerpLib.ImGui.Core;

namespace DerpLib.ImGui.Widgets;

public enum ImSplitDropdownButtonResult : byte
{
    None = 0,
    Primary = 1,
    Dropdown = 2
}

/// <summary>
/// Split button with a dropdown chevron segment. Draws as a single control (shared border/background).
/// </summary>
public static class ImSplitDropdownButton
{
    public static ImSplitDropdownButtonResult Draw(
        string label,
        float x,
        float y,
        float width,
        float height,
        float dropdownWidth,
        bool isActiveOutline = false,
        bool enabled = true)
    {
        if (dropdownWidth <= 0f)
        {
            dropdownWidth = 1f;
        }
        if (dropdownWidth >= width - 1f)
        {
            dropdownWidth = width * 0.35f;
        }

        var ctx = Im.Context;
        var style = ctx.Style;

        ctx.PushId(label);
        int mainId = ctx.GetId(0);
        int dropId = ctx.GetId(1);
        ctx.PopId();

        var fullRect = new ImRect(x, y, width, height);
        var mainRect = new ImRect(x, y, width - dropdownWidth, height);
        var dropRect = new ImRect(x + (width - dropdownWidth), y, dropdownWidth, height);

        bool hoveredMain = enabled && mainRect.Contains(Im.MousePos);
        bool hoveredDrop = enabled && dropRect.Contains(Im.MousePos);

        if (hoveredDrop)
        {
            ctx.SetHot(dropId);
        }
        else if (hoveredMain)
        {
            ctx.SetHot(mainId);
        }

        if (enabled && ctx.IsHot(mainId) && ctx.Input.MousePressed)
        {
            ctx.SetActive(mainId);
        }
        if (enabled && ctx.IsHot(dropId) && ctx.Input.MousePressed)
        {
            ctx.SetActive(dropId);
        }

        ImSplitDropdownButtonResult result = ImSplitDropdownButtonResult.None;
        if ((ctx.IsActive(mainId) || ctx.IsActive(dropId)) && ctx.Input.MouseReleased)
        {
            if (ctx.IsActive(mainId) && ctx.IsHot(mainId))
            {
                result = ImSplitDropdownButtonResult.Primary;
            }
            else if (ctx.IsActive(dropId) && ctx.IsHot(dropId))
            {
                result = ImSplitDropdownButtonResult.Dropdown;
            }

            ctx.ClearActive();
        }

        float r = style.CornerRadius;
        uint hoverColor = style.Hover;
        uint activeColor = style.Active;
        uint disabledText = style.TextDisabled;
        uint textColor = enabled ? style.TextPrimary : disabledText;
        uint chevronColor = enabled ? style.TextSecondary : disabledText;

        if (enabled)
        {
            if (ctx.IsActive(mainId))
            {
                Im.DrawRoundedRectPerCorner(mainRect.X, mainRect.Y, mainRect.Width, mainRect.Height, r, 0f, 0f, r, activeColor);
            }
            else if (ctx.IsHot(mainId))
            {
                Im.DrawRoundedRectPerCorner(mainRect.X, mainRect.Y, mainRect.Width, mainRect.Height, r, 0f, 0f, r, hoverColor);
            }

            if (ctx.IsActive(dropId))
            {
                Im.DrawRoundedRectPerCorner(dropRect.X, dropRect.Y, dropRect.Width, dropRect.Height, 0f, r, r, 0f, activeColor);
            }
            else if (ctx.IsHot(dropId))
            {
                Im.DrawRoundedRectPerCorner(dropRect.X, dropRect.Y, dropRect.Width, dropRect.Height, 0f, r, r, 0f, hoverColor);
            }
        }

        // Label (centered in main area, supports "##" label suffix)
        ReadOnlySpan<char> visible = GetVisibleLabel(label);
        if (!visible.IsEmpty)
        {
            float textWidth = ImTextMetrics.MeasureWidth(ctx.Font, visible, style.FontSize);
            float textX = mainRect.X + (mainRect.Width - textWidth) * 0.5f;
            float textY = mainRect.Y + (mainRect.Height - style.FontSize) * 0.5f;
            Im.Text(visible, textX, textY, style.FontSize, textColor);
        }

        // Chevron (centered in dropdown area)
        {
            float chevronSize = MathF.Min(dropRect.Width, dropRect.Height) * 0.34f;
            float chevronX = dropRect.X + (dropRect.Width - chevronSize) * 0.5f;
            float chevronY = dropRect.Y + (dropRect.Height - chevronSize) * 0.5f;
            ImIcons.DrawChevron(chevronX, chevronY, chevronSize, ImIcons.ChevronDirection.Down, chevronColor);
        }

        if (isActiveOutline)
        {
            Im.DrawRoundedRectStroke(fullRect.X, fullRect.Y, fullRect.Width, fullRect.Height, r, style.Primary, 2f);
        }

        return result;
    }

    private static ReadOnlySpan<char> GetVisibleLabel(string label)
    {
        if (label == null)
        {
            return ReadOnlySpan<char>.Empty;
        }

        ReadOnlySpan<char> span = label.AsSpan();
        for (int i = 0; i + 1 < span.Length; i++)
        {
            if (span[i] == '#' && span[i + 1] == '#')
            {
                return span[..i];
            }
        }

        return span;
    }
}
