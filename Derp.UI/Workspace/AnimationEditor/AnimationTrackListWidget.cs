using System.Numerics;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Widgets;
using FontAwesome.Sharp;
using Property;
using Property.Runtime;

namespace Derp.UI;

internal static class AnimationTrackListWidget
{
    private static readonly string AutoKeyIcon = ((char)IconChar.RecordVinyl).ToString();
    private static readonly string PlayIcon = ((char)IconChar.Play).ToString();
    private static readonly string PauseIcon = ((char)IconChar.Pause).ToString();
    private static readonly string GraphIcon = ((char)IconChar.ChartLine).ToString();
    private static readonly string TimelineIcon = ((char)IconChar.Timeline).ToString();
    private static readonly string ModeOneShotIcon = ((char)IconChar.ArrowRight).ToString();
    private static readonly string ModeLoopIcon = ((char)IconChar.Repeat).ToString();
    private static readonly string ModePingPongIcon = ((char)IconChar.RightLeft).ToString();

    private static readonly string ModeOneShotMenuLabel = ModeOneShotIcon + " One Shot";
    private static readonly string ModeLoopMenuLabel = ModeLoopIcon + " Loop";
    private static readonly string ModePingPongMenuLabel = ModePingPongIcon + " Ping Pong";

    private static readonly string ModeOneShotButtonLabel = ModeOneShotIcon;
    private static readonly string ModeLoopButtonLabel = ModeLoopIcon;
    private static readonly string ModePingPongButtonLabel = ModePingPongIcon;

    private const float ModeDropdownWidth = 20f;
    private const float ModeMainExtraWidth = 8f;

    public static void Draw(UiWorkspace workspace, AnimationEditorState state, AnimationDocument.AnimationTimeline? timeline, ImRect rect)
    {
        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        if (timeline == null)
        {
            Im.Text("Select a timeline.", rect.X + Im.Style.Padding, rect.Y + Im.Style.Padding, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        DrawToolbar(workspace, state, timeline, rect, out float contentY);
        DrawRows(workspace, state, timeline, rect, contentY);
    }

    private static void DrawToolbar(UiWorkspace workspace, AnimationEditorState state, AnimationDocument.AnimationTimeline timeline, ImRect rect, out float contentY)
    {
        float padding = Im.Style.Padding;
        float y = rect.Y + padding;

        float buttonRowHeight = Im.Style.MinButtonHeight;
        float rowX0 = rect.X + padding;
        float rowX1 = rect.Right - padding;
        float spacing = Im.Style.Spacing;
        float timeW = 110f;
        float iconW = buttonRowHeight;
        float xTime = rowX1 - timeW;

        Im.Context.PushId(0x414E5442); // "ANTB"

        float x = rowX0;
        if (x + iconW > xTime)
        {
            contentY = y + buttonRowHeight + Im.Style.Spacing * 2f;
            Im.Context.PopId();
            AnimationTimelineMenuWidget.Draw(state, timeline);
            return;
        }

        if (DrawToolbarIconButton("anim_auto", new ImRect(x, y, iconW, buttonRowHeight), AutoKeyIcon, state.AutoKeyEnabled))
        {
            state.AutoKeyEnabled = !state.AutoKeyEnabled;
        }

        x += iconW + spacing;
        string playIcon = state.IsPlaying ? PauseIcon : PlayIcon;
        if (DrawToolbarIconButton("anim_play", new ImRect(x, y, iconW, buttonRowHeight), playIcon, state.IsPlaying))
        {
            if (state.IsPlaying)
            {
                state.IsPlaying = false;
            }
            else
            {
                state.IsPlaying = true;
                state.PlaybackFrameAccumulator = 0f;
                state.PlaybackDirection = 1;

                int duration = Math.Max(1, timeline.DurationFrames);
                int rangeStart = Math.Clamp(timeline.WorkStartFrame, 0, duration);
                int rangeEnd = Math.Clamp(timeline.WorkEndFrame, 0, duration);
                if (rangeEnd < rangeStart)
                {
                    (rangeStart, rangeEnd) = (rangeEnd, rangeStart);
                }

                state.CurrentFrame = Math.Clamp(state.CurrentFrame, rangeStart, rangeEnd);
                if (timeline.Mode == AnimationDocument.PlaybackMode.PingPong && state.CurrentFrame >= rangeEnd && rangeEnd > rangeStart)
                {
                    state.PlaybackDirection = -1;
                }
            }
        }

        x += iconW + spacing;
        string graphIcon = state.GraphMode ? TimelineIcon : GraphIcon;
        if (DrawToolbarIconButton("anim_graph", new ImRect(x, y, iconW, buttonRowHeight), graphIcon, state.GraphMode))
        {
            state.GraphMode = !state.GraphMode;
            state.Selected = default;
        }

        x += iconW + spacing;
        float modeW = iconW + ModeMainExtraWidth + ModeDropdownWidth;
        DrawPlaybackModeSplitDropdown(state, timeline, x, y, modeW, buttonRowHeight);

        var timeButtonRect = new ImRect(xTime, y, timeW, buttonRowHeight);
        DrawTimelineMenuButton(state, timeline, timeButtonRect);

        contentY = y + buttonRowHeight + Im.Style.Spacing * 2f;

        Im.Context.PopId();
        AnimationTimelineMenuWidget.Draw(state, timeline);
    }

    private static void DrawPlaybackModeSplitDropdown(AnimationEditorState state, AnimationDocument.AnimationTimeline timeline, float x, float y, float width, float height)
    {
        string label = timeline.Mode switch
        {
            AnimationDocument.PlaybackMode.Loop => ModeLoopButtonLabel,
            AnimationDocument.PlaybackMode.PingPong => ModePingPongButtonLabel,
            _ => ModeOneShotButtonLabel
        };

        var result = ImSplitDropdownButton.Draw(
            label,
            x,
            y,
            width,
            height,
            dropdownWidth: ModeDropdownWidth,
            isActiveOutline: false,
            enabled: true);

        if (result == ImSplitDropdownButtonResult.Primary)
        {
            timeline.Mode = timeline.Mode switch
            {
                AnimationDocument.PlaybackMode.OneShot => AnimationDocument.PlaybackMode.Loop,
                AnimationDocument.PlaybackMode.Loop => AnimationDocument.PlaybackMode.PingPong,
                _ => AnimationDocument.PlaybackMode.OneShot
            };
        }
        else if (result == ImSplitDropdownButtonResult.Dropdown)
        {
            ImContextMenu.OpenAt("##anim_mode_menu", x, y + height);
        }

        if (ImContextMenu.Begin("##anim_mode_menu"))
        {
            if (ImContextMenu.Item(ModeOneShotMenuLabel))
            {
                timeline.Mode = AnimationDocument.PlaybackMode.OneShot;
            }
            if (ImContextMenu.Item(ModeLoopMenuLabel))
            {
                timeline.Mode = AnimationDocument.PlaybackMode.Loop;
            }
            if (ImContextMenu.Item(ModePingPongMenuLabel))
            {
                timeline.Mode = AnimationDocument.PlaybackMode.PingPong;
            }
            ImContextMenu.End();
        }
    }

    private static bool DrawToolbarIconButton(string widgetId, ImRect rect, string icon, bool isToggled)
    {
        var ctx = Im.Context;
        var style = Im.Style;

        int id = ctx.GetId(widgetId);
        bool hovered = rect.Contains(Im.MousePos);
        if (hovered)
        {
            ctx.SetHot(id);
        }

        bool pressed = false;
        if (ctx.IsHot(id) && ctx.Input.MousePressed)
        {
            ctx.SetActive(id);
        }
        if (ctx.IsActive(id) && ctx.Input.MouseReleased)
        {
            if (ctx.IsHot(id))
            {
                pressed = true;
            }
            ctx.ClearActive();
        }

        uint bg = isToggled ? style.Active : (ctx.IsActive(id) ? style.Active : (ctx.IsHot(id) ? style.Hover : style.Surface));
        uint border = isToggled ? style.Primary : style.Border;
        Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, style.CornerRadius, bg);
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, style.CornerRadius, border, style.BorderWidth);

        uint iconColor = isToggled ? style.TextPrimary : style.TextSecondary;
        float fontSize = style.FontSize;
        float iconWidth = Im.MeasureTextWidth(icon.AsSpan(), fontSize);
        float iconX = rect.X + (rect.Width - iconWidth) * 0.5f;
        float iconY = rect.Y + (rect.Height - fontSize) * 0.5f;
        Im.Text(icon.AsSpan(), iconX, iconY, fontSize, iconColor);

        if (isToggled)
        {
            Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, style.CornerRadius, style.Primary, 2f);
        }

        return pressed;
    }

    private static void DrawRows(UiWorkspace workspace, AnimationEditorState state, AnimationDocument.AnimationTimeline timeline, ImRect rect, float startY)
    {
        float scrollbarW = Im.Style.ScrollbarWidth;
        var viewRect = new ImRect(rect.X, startY, rect.Width, MathF.Max(0f, rect.Bottom - startY));
        var rowsRect = new ImRect(viewRect.X, viewRect.Y, MathF.Max(0f, viewRect.Width - scrollbarW), viewRect.Height);
        var scrollbarRect = new ImRect(rowsRect.Right, viewRect.Y, scrollbarW, viewRect.Height);

        float rowH = Im.Style.MinButtonHeight;
        state.TrackContentHeight = state.Rows.Count * rowH;

        var input = Im.Context.Input;
        float maxScroll = MathF.Max(0f, state.TrackContentHeight - rowsRect.Height);
        if (rowsRect.Contains(Im.MousePos) && input.ScrollDelta != 0f)
        {
            state.TrackScrollY -= input.ScrollDelta * 30f;
        }
        state.TrackScrollY = Math.Clamp(state.TrackScrollY, 0f, maxScroll);

        state.RowLayouts.Clear();

        Im.PushClipRect(rowsRect);

        for (int rowIndex = 0; rowIndex < state.Rows.Count; rowIndex++)
        {
            float yOffset = rowIndex * rowH - state.TrackScrollY;
            float y = rowsRect.Y + yOffset;
            state.RowLayouts.Add(new AnimationEditorState.RowLayout { OffsetY = yOffset, Height = rowH });

            if (y > rowsRect.Bottom || y + rowH < rowsRect.Y)
            {
                continue;
            }

            var row = state.Rows[rowIndex];
            var rowRect = new ImRect(rowsRect.X, y, rowsRect.Width, rowH);

            bool hovered = rowRect.Contains(Im.MousePos);
            uint bg = 0;
            if (hovered)
            {
                bg = ImStyle.WithAlphaF(Im.Style.Hover, 0.75f);
            }

            if (row.Kind == AnimationEditorState.RowKind.Property && row.IsAnimatable)
            {
                var binding = new AnimationDocument.AnimationBinding(
                    targetIndex: row.TargetIndex,
                    componentKind: row.ComponentKind,
                    propertyIndexHint: row.PropertyIndexHint,
                    propertyId: row.PropertyId,
                    propertyKind: row.PropertyKind);

                bool trackSelected =
                    state.SelectedTrack.TimelineId == timeline.Id &&
                    state.SelectedTrack.Binding == binding;

                if (trackSelected)
                {
                    bg = ImStyle.WithAlphaF(Im.Style.Primary, 0.14f);
                }
            }
            else if (row.Kind == AnimationEditorState.RowKind.TargetHeader)
            {
                var binding = new AnimationDocument.AnimationBinding(
                    targetIndex: row.TargetIndex,
                    componentKind: 0,
                    propertyIndexHint: 0,
                    propertyId: 0UL,
                    propertyKind: PropertyKind.Auto);

                bool trackSelected =
                    state.SelectedTrack.TimelineId == timeline.Id &&
                    state.SelectedTrack.Binding == binding;

                if (trackSelected)
                {
                    bg = ImStyle.WithAlphaF(Im.Style.Primary, 0.14f);
                }
            }
            else if (row.Kind == AnimationEditorState.RowKind.ComponentHeader)
            {
                var binding = new AnimationDocument.AnimationBinding(
                    targetIndex: row.TargetIndex,
                    componentKind: row.ComponentKind,
                    propertyIndexHint: 0,
                    propertyId: 0UL,
                    propertyKind: PropertyKind.Auto);

                bool trackSelected =
                    state.SelectedTrack.TimelineId == timeline.Id &&
                    state.SelectedTrack.Binding == binding;

                if (trackSelected)
                {
                    bg = ImStyle.WithAlphaF(Im.Style.Primary, 0.14f);
                }
            }
            else if (row.Kind == AnimationEditorState.RowKind.PropertyGroup)
            {
                var binding = new AnimationDocument.AnimationBinding(
                    targetIndex: row.TargetIndex,
                    componentKind: row.ComponentKind,
                    propertyIndexHint: row.PropertyIndexHint,
                    propertyId: row.PropertyId,
                    propertyKind: row.PropertyKind);

                bool trackSelected =
                    state.SelectedTrack.TimelineId == timeline.Id &&
                    state.SelectedTrack.Binding == binding;

                if (trackSelected)
                {
                    bg = ImStyle.WithAlphaF(Im.Style.Primary, 0.14f);
                }
            }

            if (bg != 0)
            {
                Im.DrawRect(rowRect.X, rowRect.Y, rowRect.Width, rowRect.Height, bg);
            }

            uint line = ImStyle.WithAlphaF(Im.Style.Border, 0.35f);
            Im.DrawLine(rowRect.X, rowRect.Bottom, rowRect.Right, rowRect.Bottom, 1f, line);

            float indentX = rowRect.X + Im.Style.Padding + row.Depth * 14f;
            float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;

            bool clickedToggle = false;

            if (row.Kind == AnimationEditorState.RowKind.TargetHeader)
            {
                float toggleSize = 14f;
                var toggleRect = new ImRect(indentX, rowRect.Y + (rowRect.Height - toggleSize) * 0.5f, toggleSize, toggleSize);
                bool collapsed = AnimationEditorHelpers.IsTargetCollapsed(workspace, state, row.Entity);
                ImIcons.DrawChevron(toggleRect.X, toggleRect.Y, toggleRect.Width, collapsed ? ImIcons.ChevronDirection.Right : ImIcons.ChevronDirection.Down, Im.Style.TextSecondary);
                if (toggleRect.Contains(Im.MousePos) && Im.MousePressed)
                {
                    AnimationEditorHelpers.ToggleTargetCollapsed(workspace, state, row.Entity);
                    AnimationTrackRowBuilder.Build(workspace, state, timeline);
                    clickedToggle = true;
                }
                indentX += toggleSize + Im.Style.Spacing;
            }
            else if (row.Kind == AnimationEditorState.RowKind.ComponentHeader)
            {
                float toggleSize = 14f;
                var toggleRect = new ImRect(indentX, rowRect.Y + (rowRect.Height - toggleSize) * 0.5f, toggleSize, toggleSize);
                bool collapsed = AnimationEditorHelpers.IsComponentCollapsed(workspace, state, row.Entity, row.ComponentKind);
                ImIcons.DrawChevron(toggleRect.X, toggleRect.Y, toggleRect.Width, collapsed ? ImIcons.ChevronDirection.Right : ImIcons.ChevronDirection.Down, Im.Style.TextSecondary);
                if (toggleRect.Contains(Im.MousePos) && Im.MousePressed)
                {
                    AnimationEditorHelpers.ToggleComponentCollapsed(workspace, state, row.Entity, row.ComponentKind);
                    AnimationTrackRowBuilder.Build(workspace, state, timeline);
                    clickedToggle = true;
                }
                indentX += toggleSize + Im.Style.Spacing;
            }
            else if (row.Kind == AnimationEditorState.RowKind.PropertyGroup)
            {
                float toggleSize = 14f;
                var toggleRect = new ImRect(indentX, rowRect.Y + (rowRect.Height - toggleSize) * 0.5f, toggleSize, toggleSize);
                bool collapsed = AnimationEditorHelpers.IsGroupCollapsed(state, row.ChannelGroupId);
                ImIcons.DrawChevron(toggleRect.X, toggleRect.Y, toggleRect.Width, collapsed ? ImIcons.ChevronDirection.Right : ImIcons.ChevronDirection.Down, Im.Style.TextSecondary);
                if (toggleRect.Contains(Im.MousePos) && Im.MousePressed)
                {
                    AnimationEditorHelpers.ToggleGroupCollapsed(state, row.ChannelGroupId);
                    AnimationTrackRowBuilder.Build(workspace, state, timeline);
                    clickedToggle = true;
                }
                indentX += toggleSize + Im.Style.Spacing;
            }

            uint textColor =
                row.Kind == AnimationEditorState.RowKind.TargetHeader ? Im.Style.TextPrimary :
                row.Kind == AnimationEditorState.RowKind.ComponentHeader ? Im.Style.TextPrimary :
                Im.Style.TextSecondary;

            if (row.Kind == AnimationEditorState.RowKind.TargetHeader)
            {
                AnimationEditorText.DrawEntityLabel(workspace, row.Entity, indentX, textY, textColor);
            }
            else
            {
                bool isChannelRow =
                    row.Kind == AnimationEditorState.RowKind.Property &&
                    row.IsAnimatable &&
                    row.ChannelCount > 0 &&
                    row.ChannelIndex > 0;

                if (!isChannelRow)
                {
                    Im.Text(row.Label.AsSpan(), indentX, textY, Im.Style.FontSize, textColor);
                }
            }

            if (!clickedToggle &&
                hovered &&
                Im.MousePressed &&
                (row.Kind == AnimationEditorState.RowKind.TargetHeader || row.Kind == AnimationEditorState.RowKind.ComponentHeader) &&
                row.TargetIndex >= 0)
            {
                state.Selected = default;
                state.SelectedTrack = new AnimationEditorState.TrackSelection
                {
                    TimelineId = timeline.Id,
                    Binding =
                        row.Kind == AnimationEditorState.RowKind.TargetHeader
                            ? new AnimationDocument.AnimationBinding(
                                targetIndex: row.TargetIndex,
                                componentKind: 0,
                                propertyIndexHint: 0,
                                propertyId: 0UL,
                                propertyKind: PropertyKind.Auto)
                            : new AnimationDocument.AnimationBinding(
                                targetIndex: row.TargetIndex,
                                componentKind: row.ComponentKind,
                                propertyIndexHint: 0,
                                propertyId: 0UL,
                                propertyKind: PropertyKind.Auto)
                };

                state.GraphScopeChannelGroupKey = 0UL;
            }

            if ((row.Kind == AnimationEditorState.RowKind.Property || row.Kind == AnimationEditorState.RowKind.PropertyGroup) && row.IsAnimatable)
            {
                DrawPropertyValue(workspace, state, timeline, row, rowRect, out ImRect valueRect, out ImRect keyIconRect);

                bool drawChannelLabel = false;
                if (row.Kind == AnimationEditorState.RowKind.PropertyGroup)
                {
                    drawChannelLabel = !AnimationEditorHelpers.IsGroupCollapsed(state, row.ChannelGroupId);
                }
                else if (row.Kind == AnimationEditorState.RowKind.Property && row.ChannelCount > 0 && row.ChannelIndex > 0)
                {
                    drawChannelLabel = true;
                }

                if (drawChannelLabel)
                {
                    ReadOnlySpan<char> channelLabel = GetChannelLabel(row.ChannelIndex);
                    if (!channelLabel.IsEmpty)
                    {
                        float channelX = valueRect.X - 16f;
                        Im.Text(channelLabel, channelX, textY, Im.Style.FontSize, Im.Style.TextSecondary);
                    }
                }

                if (!clickedToggle)
                {
                    var bindingForKey = new AnimationDocument.AnimationBinding(
                        targetIndex: row.TargetIndex,
                        componentKind: row.ComponentKind,
                        propertyIndexHint: row.PropertyIndexHint,
                        propertyId: row.PropertyId,
                        propertyKind: row.PropertyKind);

                    var track = AnimationEditorHelpers.FindTrack(timeline, bindingForKey);
                    bool hasTrack = track != null;
                    int durationFrames = Math.Max(1, timeline.DurationFrames);
                    int frame = Math.Clamp(state.CurrentFrame, 0, durationFrames);

                    bool hasKey = false;
                    if (track != null)
                    {
                        for (int k = 0; k < track.Keys.Count; k++)
                        {
                            if (track.Keys[k].Frame == frame)
                            {
                                hasKey = true;
                                break;
                            }
                        }
                    }

                    DrawKeyIconInInput(keyIconRect, filled: hasKey, highlighted: hasTrack);

                    if (keyIconRect.Contains(Im.MousePos) && Im.MousePressed)
                    {
                        workspace.NotifyInspectorAnimationInteraction();

                        if (hasKey && track != null)
                        {
                            bool selectionOnThisTrack =
                                state.Selected.TimelineId == timeline.Id &&
                                state.Selected.Binding == bindingForKey;
                            int selectedIndexBefore = selectionOnThisTrack ? state.Selected.KeyIndex : -1;

                            int removedIndex = -1;
                            for (int k = 0; k < track.Keys.Count; k++)
                            {
                                if (track.Keys[k].Frame == frame)
                                {
                                    removedIndex = k;
                                    track.Keys.RemoveAt(k);
                                    break;
                                }
                            }

                            if (removedIndex >= 0)
                            {
                                for (int s = 0; s < state.SelectedKeyFrames.Count; s++)
                                {
                                    var selected = state.SelectedKeyFrames[s];
                                    if (selected.TimelineId == timeline.Id && selected.Binding == bindingForKey && selected.Frame == frame)
                                    {
                                        state.SelectedKeyFrames.RemoveAt(s);
                                        s--;
                                    }
                                }

                                if (selectionOnThisTrack && selectedIndexBefore >= 0)
                                {
                                    if (selectedIndexBefore == removedIndex)
                                    {
                                        state.Selected = default;
                                    }
                                    else if (selectedIndexBefore > removedIndex)
                                    {
                                        int newIndex = selectedIndexBefore - 1;
                                        if ((uint)newIndex < (uint)track.Keys.Count)
                                        {
                                            state.Selected = new AnimationEditorState.SelectedKey
                                            {
                                                TimelineId = timeline.Id,
                                                Binding = bindingForKey,
                                                KeyIndex = newIndex
                                            };
                                        }
                                        else
                                        {
                                            state.Selected = default;
                                        }
                                    }
                                }

                                if (track.Keys.Count <= 0)
                                {
                                    RemoveTrack(timeline, track);
                                    if (state.SelectedTrack.TimelineId == timeline.Id && state.SelectedTrack.Binding == bindingForKey)
                                    {
                                        state.SelectedTrack = default;
                                    }
                                }
                            }
                        }
                        else
                        {
                            AnimationEditorHelpers.AddOrUpdateKeyframeAtCurrentFrame(workspace, state, timeline, bindingForKey, selectKeyframe: false);
                        }

                        state.PreviewDirty = true;
                    }
                }

                if (!clickedToggle && hovered && Im.MousePressed && !valueRect.Contains(Im.MousePos))
                {
                    var binding = new AnimationDocument.AnimationBinding(
                        targetIndex: row.TargetIndex,
                        componentKind: row.ComponentKind,
                        propertyIndexHint: row.PropertyIndexHint,
                        propertyId: row.PropertyId,
                        propertyKind: row.PropertyKind);

                    state.Selected = default;
                    state.SelectedTrack = new AnimationEditorState.TrackSelection
                    {
                        TimelineId = timeline.Id,
                        Binding = binding
                    };

                    state.GraphScopeChannelGroupKey =
                        row.Kind == AnimationEditorState.RowKind.PropertyGroup ? row.ChannelGroupId : 0UL;
                }
            }
        }

        Im.PopClipRect();

        int scrollbarId = Im.Context.GetId("anim_track_scrollbar");
        ImScrollbar.DrawVertical(scrollbarId, scrollbarRect, ref state.TrackScrollY, rowsRect.Height, state.TrackContentHeight);
    }

    private static void DrawTimelineMenuButton(AnimationEditorState state, AnimationDocument.AnimationTimeline timeline, ImRect rect)
    {
        uint bg = ImStyle.Lerp(Im.Style.Background, 0xFFFFFFFF, 0.08f);
        uint border = ImStyle.WithAlphaF(Im.Style.Border, 0.8f);
        Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, bg);
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, border, Im.Style.BorderWidth);

        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        float textX = rect.X + 10f;
        AnimationEditorText.DrawTimecode(state.CurrentFrame, timeline.SnapFps, textX, textY, Im.Style.TextPrimary);

        float iconSize = 10f;
        float iconX = rect.Right - 12f - iconSize;
        float iconY = rect.Y + (rect.Height - iconSize) * 0.5f;
        ImIcons.DrawChevron(iconX, iconY, iconSize, ImIcons.ChevronDirection.Down, Im.Style.TextSecondary);

        if (rect.Contains(Im.MousePos) && Im.MousePressed)
        {
            if (state.TimelineMenuOpen)
            {
                state.TimelineMenuOpen = false;
                return;
            }

            Vector2 translation = Im.CurrentTranslation;
            state.TimelineMenuPosViewport = new Vector2(rect.X + translation.X, rect.Bottom + translation.Y + 2f);
            state.TimelineMenuOpen = true;
            state.TimelineMenuOpenFrame = Im.Context.FrameCount;
            state.TimelineMenuNeedsSync = true;
        }
    }

    private static void DrawPropertyValue(UiWorkspace workspace, AnimationEditorState state, AnimationDocument.AnimationTimeline timeline, in AnimationEditorState.Row row, ImRect rowRect, out ImRect valueRect, out ImRect keyIconRect)
    {
        float rightPad = Im.Style.Padding;
        float maxWidth = 92f;
        float minWidth = 64f;
        float available = rowRect.Width - rightPad * 2f;
        float inputW = Math.Clamp(available * 0.42f, minWidth, maxWidth);
        float inputH = rowRect.Height;
        float inputX = rowRect.Right - rightPad - inputW;
        float inputY = rowRect.Y;
        valueRect = new ImRect(inputX, inputY, inputW, inputH);
        const float keyIconWidth = 18f;
        keyIconRect = new ImRect(valueRect.Right - keyIconWidth, valueRect.Y, keyIconWidth, valueRect.Height);

        var binding = new AnimationDocument.AnimationBinding(
            targetIndex: row.TargetIndex,
            componentKind: row.ComponentKind,
            propertyIndexHint: row.PropertyIndexHint,
            propertyId: row.PropertyId,
            propertyKind: row.PropertyKind);

        if (binding.ComponentKind == PrefabInstanceComponent.Api.PoolIdConst &&
            AnimationBindingIds.TryGetPrefabVariableId(binding.PropertyId, out ushort variableId))
        {
            DrawPrefabInstanceVariableValue(workspace, row.Entity, variableId, binding.PropertyKind, valueRect, keyIconRect);
            return;
        }

        if (!AnimationEditorHelpers.TryResolveSlot(workspace, timeline, binding, out PropertySlot slot))
        {
            return;
        }

        var ctx = Im.Context;
        int uniqueId = unchecked((int)(row.PropertyId ^ ((ulong)row.TargetIndex << 32) ^ ((ulong)row.ComponentKind << 48)));
        ctx.PushId(uniqueId);

        PropertyValue value = AnimationEditorHelpers.ReadValue(workspace.PropertyWorld, slot);
        switch (slot.Kind)
        {
            case PropertyKind.Float:
            {
                float floatValue = value.Float;
                float min = float.MinValue;
                float max = float.MaxValue;
                if (PropertyDispatcher.TryGetInfo(slot.Component, (int)slot.PropertyIndex, out var info))
                {
                    if (!float.IsNaN(info.Min))
                    {
                        min = info.Min;
                    }
                    if (!float.IsNaN(info.Max))
                    {
                        max = info.Max;
                    }
                }

                if (ImScalarInput.DrawAt("val", valueRect.X, valueRect.Y, valueRect.Width, keyIconRect.Width, ref floatValue, min, max, "0.##"))
                {
                    int widgetId = ctx.GetId("val");
                    bool isEditing = ctx.IsActive(widgetId) || ctx.IsFocused(widgetId);
                    workspace.Commands.SetPropertyValue(widgetId, isEditing, slot, PropertyValue.FromFloat(floatValue));
                }
                else
                {
                    int widgetId = ctx.GetId("val");
                    bool isEditing = ctx.IsActive(widgetId) || ctx.IsFocused(widgetId);
                    workspace.Commands.NotifyPropertyWidgetState(widgetId, isEditing);
                }
                break;
            }
            case PropertyKind.Int:
            {
                int intValue = value.Int;
                float floatValue = intValue;
                if (ImScalarInput.DrawAt("val", valueRect.X, valueRect.Y, valueRect.Width, keyIconRect.Width, ref floatValue, min: int.MinValue, max: int.MaxValue, format: "F0"))
                {
                    intValue = (int)MathF.Round(floatValue);
                    int widgetId = ctx.GetId("val");
                    bool isEditing = ctx.IsActive(widgetId) || ctx.IsFocused(widgetId);
                    workspace.Commands.SetPropertyValue(widgetId, isEditing, slot, PropertyValue.FromInt(intValue));
                }
                else
                {
                    int widgetId = ctx.GetId("val");
                    bool isEditing = ctx.IsActive(widgetId) || ctx.IsFocused(widgetId);
                    workspace.Commands.NotifyPropertyWidgetState(widgetId, isEditing);
                }
                break;
            }
            case PropertyKind.Bool:
            {
                bool boolValue = value.Bool;
                uint bg = Im.Style.Surface;
                uint border = Im.Style.Border;
                Im.DrawRoundedRect(valueRect.X, valueRect.Y, valueRect.Width, valueRect.Height, Im.Style.CornerRadius, bg);
                Im.DrawRoundedRectStroke(valueRect.X, valueRect.Y, valueRect.Width, valueRect.Height, Im.Style.CornerRadius, border, Im.Style.BorderWidth);

                float checkboxX = valueRect.X + Im.Style.Padding;
                float checkboxY = valueRect.Y + (valueRect.Height - Im.Style.CheckboxSize) * 0.5f;
                if (Im.Checkbox("bool", ref boolValue, checkboxX, checkboxY))
                {
                    int widgetId = ctx.GetId("bool");
                    workspace.Commands.SetPropertyValue(widgetId, isEditing: false, slot, PropertyValue.FromBool(boolValue));
                }
                workspace.Commands.NotifyPropertyWidgetState(ctx.GetId("bool"), isEditing: false);
                break;
            }
        }

        ctx.PopId();
    }

    private static void DrawPrefabInstanceVariableValue(UiWorkspace workspace, EntityId instanceEntity, ushort variableId, PropertyKind kind, ImRect valueRect, ImRect keyIconRect)
    {
        var ctx = Im.Context;
        int uniqueId = unchecked((int)((ulong)variableId ^ ((ulong)kind << 32) ^ ((ulong)PrefabInstanceComponent.Api.PoolIdConst << 48)));
        ctx.PushId(uniqueId);

        PropertyValue current = default;
        TryReadPrefabInstanceVariableValue(workspace, instanceEntity, variableId, out current);

        switch (kind)
        {
            case PropertyKind.Float:
            {
                float floatValue = current.Float;
                    if (ImScalarInput.DrawAt("val", valueRect.X, valueRect.Y, valueRect.Width, keyIconRect.Width, ref floatValue, min: float.MinValue, max: float.MaxValue, format: "0.##"))
                    {
                        int widgetId = ctx.GetId("val");
                        bool isEditing = ctx.IsActive(widgetId) || ctx.IsFocused(widgetId);
                        workspace.Commands.SetPrefabInstanceVariableValue(widgetId, isEditing, instanceEntity, variableId, kind, PropertyValue.FromFloat(floatValue));
                    }
                else
                {
                    int widgetId = ctx.GetId("val");
                    bool isEditing = ctx.IsActive(widgetId) || ctx.IsFocused(widgetId);
                    workspace.Commands.NotifyPropertyWidgetState(widgetId, isEditing);
                }
                break;
            }
            case PropertyKind.Int:
            {
                int intValue = current.Int;
                float floatValue = intValue;
                    if (ImScalarInput.DrawAt("val", valueRect.X, valueRect.Y, valueRect.Width, keyIconRect.Width, ref floatValue, min: int.MinValue, max: int.MaxValue, format: "F0"))
                    {
                        intValue = (int)MathF.Round(floatValue);
                        int widgetId = ctx.GetId("val");
                        bool isEditing = ctx.IsActive(widgetId) || ctx.IsFocused(widgetId);
                        workspace.Commands.SetPrefabInstanceVariableValue(widgetId, isEditing, instanceEntity, variableId, kind, PropertyValue.FromInt(intValue));
                    }
                else
                {
                    int widgetId = ctx.GetId("val");
                    bool isEditing = ctx.IsActive(widgetId) || ctx.IsFocused(widgetId);
                    workspace.Commands.NotifyPropertyWidgetState(widgetId, isEditing);
                }
                break;
            }
            case PropertyKind.Bool:
            {
                bool boolValue = current.Bool;
                uint bg = Im.Style.Surface;
                uint border = Im.Style.Border;
                Im.DrawRoundedRect(valueRect.X, valueRect.Y, valueRect.Width, valueRect.Height, Im.Style.CornerRadius, bg);
                Im.DrawRoundedRectStroke(valueRect.X, valueRect.Y, valueRect.Width, valueRect.Height, Im.Style.CornerRadius, border, Im.Style.BorderWidth);

                float checkboxX = valueRect.X + Im.Style.Padding;
                float checkboxY = valueRect.Y + (valueRect.Height - Im.Style.CheckboxSize) * 0.5f;
                    if (Im.Checkbox("bool", ref boolValue, checkboxX, checkboxY))
                    {
                        int widgetId = ctx.GetId("bool");
                        workspace.Commands.SetPrefabInstanceVariableValue(widgetId, isEditing: false, instanceEntity, variableId, kind, PropertyValue.FromBool(boolValue));
                    }
                workspace.Commands.NotifyPropertyWidgetState(ctx.GetId("bool"), isEditing: false);
                break;
            }
            default:
            {
                Im.Text("—".AsSpan(), valueRect.X + Im.Style.Padding, valueRect.Y + (valueRect.Height - Im.Style.FontSize) * 0.5f, Im.Style.FontSize, Im.Style.TextSecondary);
                break;
            }
        }

        ctx.PopId();
    }

    private static bool TryReadPrefabInstanceVariableValue(UiWorkspace workspace, EntityId instanceEntity, ushort variableId, out PropertyValue value)
    {
        value = default;

        if (instanceEntity.IsNull)
        {
            return false;
        }

        if (!workspace.World.TryGetComponent(instanceEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) || !instanceAny.IsValid)
        {
            return false;
        }

        var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
        var instance = PrefabInstanceComponent.Api.FromHandle(workspace.PropertyWorld, instanceHandle);
        if (!instance.IsAlive)
        {
            return false;
        }

        ushort count = instance.ValueCount;
        if (count > PrefabInstanceComponent.MaxVariables)
        {
            count = PrefabInstanceComponent.MaxVariables;
        }

        ReadOnlySpan<ushort> ids = instance.VariableIdReadOnlySpan();
        ReadOnlySpan<PropertyValue> values = instance.ValueReadOnlySpan();
        ulong overrideMask = instance.OverrideMask;

        int instanceIndex = -1;
        for (int i = 0; i < count; i++)
        {
            if (ids[i] == variableId)
            {
                instanceIndex = i;
                break;
            }
        }

        if (instanceIndex >= 0)
        {
            bool isOverridden = (overrideMask & (1UL << instanceIndex)) != 0;
            if (isOverridden)
            {
                value = values[instanceIndex];
                return true;
            }
        }

        uint sourcePrefabStableId = instance.SourcePrefabStableId;
        if (sourcePrefabStableId != 0)
        {
            EntityId sourcePrefab = workspace.World.GetEntityByStableId(sourcePrefabStableId);
            if (!sourcePrefab.IsNull &&
                workspace.World.GetNodeType(sourcePrefab) == UiNodeType.Prefab &&
                workspace.World.TryGetComponent(sourcePrefab, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) &&
                varsAny.IsValid)
            {
                var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
                var vars = PrefabVariablesComponent.Api.FromHandle(workspace.PropertyWorld, varsHandle);
                if (vars.IsAlive && vars.VariableCount > 0)
                {
                    ushort varCount = vars.VariableCount;
                    if (varCount > PrefabVariablesComponent.MaxVariables)
                    {
                        varCount = PrefabVariablesComponent.MaxVariables;
                    }

                    ReadOnlySpan<ushort> varIds = vars.VariableIdReadOnlySpan();
                    ReadOnlySpan<PropertyValue> defaults = vars.DefaultValueReadOnlySpan();
                    for (int i = 0; i < varCount; i++)
                    {
                        if (varIds[i] == variableId)
                        {
                            value = defaults[i];
                            return true;
                        }
                    }
                }
            }
        }

        if (instanceIndex >= 0)
        {
            value = values[instanceIndex];
            return true;
        }

        return false;
    }

    private static void DrawKeyIconInInput(ImRect rect, bool filled, bool highlighted)
    {
        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        uint color = highlighted ? Im.Style.Primary : Im.Style.TextSecondary;
        float size = MathF.Min(rect.Width, rect.Height) * 0.46f;
        float cx = rect.X + rect.Width * 0.5f;
        float cy = rect.Y + rect.Height * 0.5f;

        if (filled)
        {
            AnimationEditorHelpers.DrawFilledDiamond(cx, cy, size, color);
            return;
        }

        float half = size * 0.5f;
        Im.DrawLine(cx, cy - half, cx + half, cy, 1f, color);
        Im.DrawLine(cx + half, cy, cx, cy + half, 1f, color);
        Im.DrawLine(cx, cy + half, cx - half, cy, 1f, color);
        Im.DrawLine(cx - half, cy, cx, cy - half, 1f, color);
    }

    private static void RemoveTrack(AnimationDocument.AnimationTimeline timeline, AnimationDocument.AnimationTrack track)
    {
        for (int i = 0; i < timeline.Tracks.Count; i++)
        {
            if (ReferenceEquals(timeline.Tracks[i], track))
            {
                timeline.Tracks.RemoveAt(i);
                return;
            }
        }
    }

    private static ReadOnlySpan<char> GetChannelLabel(ushort channelIndex)
    {
        return channelIndex switch
        {
            0 => "X".AsSpan(),
            1 => "Y".AsSpan(),
            2 => "Z".AsSpan(),
            3 => "W".AsSpan(),
            _ => ReadOnlySpan<char>.Empty
        };
    }
}
