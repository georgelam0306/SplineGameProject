using System;
using Core;
using FontAwesome.Sharp;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Widgets;

namespace Derp.UI;

internal static class AnimationsLibraryWidget
{
    private const int RenameMaxChars = 64;

    private enum RenameTargetKind : byte
    {
        None = 0,
        Timeline = 1,
        StateMachine = 2
    }

    private static readonly string TimelineIcon = ((char)IconChar.Timeline).ToString();
    private static readonly string StateMachineIcon = ((char)IconChar.CircleNodes).ToString();
    private static readonly string AddIcon = ((char)IconChar.Plus).ToString();

    private static readonly string AddButtonLabel = AddIcon;
    private static readonly string AddTimelineMenuLabel = TimelineIcon + " Add Timeline";
    private static readonly string AddStateMachineMenuLabel = StateMachineIcon + " Add State Machine";

    private static RenameTargetKind _renameTargetKind;
    private static int _renameTargetId;
    private static readonly char[] _renameBuffer = new char[RenameMaxChars];
    private static int _renameLength;
    private static bool _renameNeedsFocus;
    private static int _renameStartFrame;

    public static void Draw(
        UiWorkspace workspace,
        AnimationDocument? animations,
        StateMachineDefinitionComponent.ViewProxy stateMachines,
        bool hasStateMachines,
        AnimationEditorState animationEditorState,
        ref AnimationsLibrarySelectionKind selectionKind,
        ImRect rect)
    {
        Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.Background);

        const float margin = 2f;
        var panelRect = new ImRect(rect.X + margin, rect.Y + margin, MathF.Max(0f, rect.Width - margin * 2f), MathF.Max(0f, rect.Height - margin * 2f));

        var headerRect = AnimationEditorPanel.DrawPanel(panelRect, "Animations", out ImRect contentRect);

        float addWidth = 26f;
        float addX = headerRect.Right - addWidth;
        float addY = headerRect.Y + (headerRect.Height - Im.Style.MinButtonHeight) * 0.5f;
        Im.Context.PushId("anim_library_add");
        bool addPressed = Im.Button(AddButtonLabel, addX, addY, addWidth, Im.Style.MinButtonHeight);
        Im.Context.PopId();
        if (addPressed)
        {
            ImContextMenu.OpenAt("anim_library_add_menu", addX, addY + Im.Style.MinButtonHeight);
        }

        float y = contentRect.Y;

        if (animations == null || !hasStateMachines)
        {
            Im.Text("No active prefab.", contentRect.X + Im.Style.Padding, contentRect.Y + Im.Style.Padding, Im.Style.FontSize, Im.Style.TextSecondary);
            return;
        }

        if (AnimationsLibraryDragDrop.HasActiveDrag &&
            AnimationsLibraryDragDrop.Kind == AnimationsLibraryDragDrop.DragKind.Timeline &&
            AnimationsLibraryDragDrop.ReleaseFrame == Im.Context.FrameCount &&
            !AnimationsLibraryDragDrop.ThresholdMet)
        {
            animations.SelectedTimelineId = AnimationsLibraryDragDrop.TimelineId;
            selectionKind = AnimationsLibrarySelectionKind.Timeline;
            animationEditorState.Selected = default;
            animationEditorState.SelectedTrack = default;
            animationEditorState.CurrentFrame = 0;
            animationEditorState.ViewStartFrame = 0;
            workspace.NotifyInspectorAnimationInteraction();
        }

        if (ImContextMenu.Begin("anim_library_add_menu"))
        {
            if (ImContextMenu.Item(AddTimelineMenuLabel))
            {
                int count = animations.Timelines.Count + 1;
                animations.CreateTimeline(name: $"Timeline {count}", durationFrames: 300, snapFps: 60, parentFolderId: animations.RootFolderId);
                selectionKind = AnimationsLibrarySelectionKind.Timeline;
            }

            if (ImContextMenu.Item(AddStateMachineMenuLabel))
            {
                if (TryAddStateMachine(stateMachines, out ushort createdId))
                {
                    stateMachines.SelectedMachineId = createdId;
                }
                selectionKind = AnimationsLibrarySelectionKind.StateMachine;
            }

            ImContextMenu.End();
        }

        HandleRenameCommitOrCancel(animations, stateMachines);

        if (stateMachines.MachineCount > 0)
        {
            y = DrawSectionLabel(contentRect, y, StateMachineIcon + " State Machines");
            y = DrawStateMachineRows(workspace, contentRect, stateMachines, selectionKind, y, ref selectionKind);
        }

        if (animations.Timelines.Count > 0)
        {
            y = DrawSectionLabel(contentRect, y, TimelineIcon + " Timelines");
            DrawTimelineRows(workspace, contentRect, animations, animationEditorState, selectionKind, y, ref selectionKind);
        }
    }

    private static float DrawSectionLabel(ImRect contentRect, float y, string label)
    {
        var rect = new ImRect(contentRect.X, y, contentRect.Width, AnimationEditorLayout.RowHeight);
        if (rect.Bottom > contentRect.Bottom)
        {
            return y;
        }

        uint bg = ImStyle.WithAlphaF(Im.Style.Surface, 0.5f);
        Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, bg);
        Im.Text(label.AsSpan(), rect.X + Im.Style.Padding, rect.Y + 4f, Im.Style.FontSize, Im.Style.TextSecondary);
        return y + AnimationEditorLayout.RowHeight;
    }

    private static float DrawStateMachineRows(
        UiWorkspace workspace,
        ImRect contentRect,
        StateMachineDefinitionComponent.ViewProxy stateMachines,
        AnimationsLibrarySelectionKind selectionKind,
        float y,
        ref AnimationsLibrarySelectionKind selectionKindRef)
    {
        int count = stateMachines.MachineCount;
        ReadOnlySpan<ushort> machineIds = stateMachines.MachineIdReadOnlySpan();
        ReadOnlySpan<StringHandle> machineNames = stateMachines.MachineNameReadOnlySpan();

        for (int i = 0; i < count; i++)
        {
            ushort machineId = machineIds[i];
            StringHandle machineNameHandle = machineNames[i];
            bool selected =
                selectionKind == AnimationsLibrarySelectionKind.StateMachine &&
                stateMachines.SelectedMachineId == machineId;

            var rowRect = new ImRect(contentRect.X, y, contentRect.Width, AnimationEditorLayout.RowHeight);
            if (rowRect.Bottom > contentRect.Bottom)
            {
                break;
            }

            if (selected)
            {
                Im.DrawRect(rowRect.X, rowRect.Y, rowRect.Width, rowRect.Height, Im.Style.Primary);
            }

            var input = Im.Context.Input;
            bool isRenamingThis = _renameTargetKind == RenameTargetKind.StateMachine && _renameTargetId == machineId;
            bool hovered = rowRect.Contains(Im.MousePos);
            bool clicked = hovered && Im.MousePressed && !isRenamingThis;
            if (clicked)
            {
                stateMachines.SelectedMachineId = machineId;
                selectionKindRef = AnimationsLibrarySelectionKind.StateMachine;
                workspace.NotifyInspectorAnimationInteraction();

                if (input.IsDoubleClick)
                {
                    string machineName = machineNameHandle;
                    BeginInlineRename(RenameTargetKind.StateMachine, machineId, machineName);
                }
            }

            uint textColor = selected ? 0xFFFFFFFF : Im.Style.TextPrimary;
            float textX = rowRect.X + Im.Style.Padding;
            float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
            if (isRenamingThis)
            {
                DrawInlineRenameInput(textX, rowRect, textColor);
            }
            else
            {
                string machineName = machineNameHandle;
                Im.Text(machineName.AsSpan(), textX, textY, Im.Style.FontSize, textColor);
            }

            y += AnimationEditorLayout.RowHeight;
        }

        return y;
    }

    private static void DrawTimelineRows(
        UiWorkspace workspace,
        ImRect contentRect,
        AnimationDocument animations,
        AnimationEditorState animationEditorState,
        AnimationsLibrarySelectionKind selectionKind,
        float y,
        ref AnimationsLibrarySelectionKind selectionKindRef)
    {
        if (!animations.TryGetFolderById(animations.RootFolderId, out var root) || root == null)
        {
            return;
        }

        for (int i = 0; i < root.Children.Count; i++)
        {
            var entry = root.Children[i];
            if (entry.Kind != AnimationDocument.LibraryEntryKind.Timeline)
            {
                continue;
            }

            var timeline = animations.GetTimelineById(entry.Id);
            bool selected =
                selectionKind == AnimationsLibrarySelectionKind.Timeline &&
                animations.SelectedTimelineId == timeline.Id;

            var rowRect = new ImRect(contentRect.X, y, contentRect.Width, AnimationEditorLayout.RowHeight);
            if (rowRect.Bottom > contentRect.Bottom)
            {
                break;
            }

            if (selected)
            {
                Im.DrawRect(rowRect.X, rowRect.Y, rowRect.Width, rowRect.Height, Im.Style.Primary);
            }

            var input = Im.Context.Input;
            bool isRenamingThis = _renameTargetKind == RenameTargetKind.Timeline && _renameTargetId == timeline.Id;
            bool hovered = rowRect.Contains(Im.MousePos);
            bool clicked = hovered && Im.MousePressed && !isRenamingThis;
            if (clicked)
            {
                if (input.IsDoubleClick)
                {
                    AnimationsLibraryDragDrop.Clear();
                    animations.SelectedTimelineId = timeline.Id;
                    selectionKindRef = AnimationsLibrarySelectionKind.Timeline;
                    animationEditorState.Selected = default;
                    animationEditorState.SelectedTrack = default;
                    animationEditorState.CurrentFrame = 0;
                    animationEditorState.ViewStartFrame = 0;
                    workspace.NotifyInspectorAnimationInteraction();
                    BeginInlineRename(RenameTargetKind.Timeline, timeline.Id, timeline.Name);
                }
                else
                {
                    AnimationsLibraryDragDrop.BeginTimelineDrag(timeline.Id, Im.MousePos);
                }
            }

            uint textColor = selected ? 0xFFFFFFFF : Im.Style.TextPrimary;
            float textX = rowRect.X + Im.Style.Padding;
            float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
            if (isRenamingThis)
            {
                DrawInlineRenameInput(textX, rowRect, textColor);
            }
            else
            {
                Im.Text(timeline.Name.AsSpan(), textX, textY, Im.Style.FontSize, textColor);
            }

            y += AnimationEditorLayout.RowHeight;
        }
    }

    private static void BeginInlineRename(RenameTargetKind targetKind, int targetId, string currentName)
    {
        _renameTargetKind = targetKind;
        _renameTargetId = targetId;
        _renameStartFrame = Im.Context.FrameCount;
        _renameLength = 0;

        if (!string.IsNullOrEmpty(currentName))
        {
            int copyLen = Math.Min(currentName.Length, RenameMaxChars - 1);
            currentName.AsSpan(0, copyLen).CopyTo(_renameBuffer);
            _renameLength = copyLen;
        }

        _renameNeedsFocus = true;
    }

    private static void DrawInlineRenameInput(float textX, ImRect rowRect, uint textColor)
    {
        float maxWidth = MathF.Max(20f, rowRect.Width - Im.Style.Padding * 2f);

        var ctx = Im.Context;
        ctx.PushId((int)_renameTargetKind);
        ctx.PushId(_renameTargetId);
        int widgetId = ctx.GetId("anim_library_rename");

        if (_renameNeedsFocus)
        {
            ImTextArea.ClearState(widgetId);
            ctx.RequestFocus(widgetId);
            ctx.SetActive(widgetId);
            ctx.ResetCaretBlink();
            _renameNeedsFocus = false;
        }

        bool focused = ctx.IsFocused(widgetId);
        uint bg = ImStyle.WithAlpha(Im.Style.Surface, 220);
        uint border = focused ? ImStyle.WithAlpha(Im.Style.Primary, 200) : ImStyle.WithAlpha(Im.Style.Border, 160);
        Im.DrawRoundedRect(textX - 2f, rowRect.Y + 2f, maxWidth + 4f, rowRect.Height - 4f, 4f, bg);
        Im.DrawRoundedRectStroke(textX - 2f, rowRect.Y + 2f, maxWidth + 4f, rowRect.Height - 4f, 4f, border, 1.25f);

        ref var style = ref Im.Style;
        float savedPadding = style.Padding;
        style.Padding = 0f;

        bool changed = ImTextArea.DrawAt(
            "anim_library_rename",
            _renameBuffer,
            ref _renameLength,
            RenameMaxChars,
            textX,
            rowRect.Y,
            maxWidth,
            rowRect.Height,
            wordWrap: false,
            fontSizePx: style.FontSize,
            flags: ImTextArea.ImTextAreaFlags.NoBackground | ImTextArea.ImTextAreaFlags.NoBorder | ImTextArea.ImTextAreaFlags.NoRounding | ImTextArea.ImTextAreaFlags.SingleLine,
            lineHeightPx: style.FontSize,
            letterSpacingPx: 0f,
            alignX: 0,
            alignY: 1,
            textColor: textColor);

        style.Padding = savedPadding;
        ctx.PopId();
        ctx.PopId();

        _ = changed;
    }

    private static void HandleRenameCommitOrCancel(AnimationDocument animations, StateMachineDefinitionComponent.ViewProxy stateMachines)
    {
        if (_renameTargetKind == RenameTargetKind.None || _renameTargetId == 0)
        {
            return;
        }

        var ctx = Im.Context;
        ctx.PushId((int)_renameTargetKind);
        ctx.PushId(_renameTargetId);
        int widgetId = ctx.GetId("anim_library_rename");
        bool focused = ctx.IsFocused(widgetId);
        ctx.PopId();
        ctx.PopId();

        var input = ctx.Input;
        if (input.KeyEscape)
        {
            _renameTargetKind = RenameTargetKind.None;
            _renameTargetId = 0;
            _renameLength = 0;
            _renameNeedsFocus = false;
            _renameStartFrame = 0;
            return;
        }

        bool canCommitOutsideClick = ctx.FrameCount != _renameStartFrame;
        bool commit = input.KeyEnter || (canCommitOutsideClick && !focused && Im.MousePressed);
        if (!commit)
        {
            return;
        }

        string committed = _renameLength <= 0 ? string.Empty : new string(_renameBuffer, 0, _renameLength);
        if (_renameTargetKind == RenameTargetKind.Timeline)
        {
            if (animations.TryGetTimelineById(_renameTargetId, out var timeline) && timeline != null)
            {
                timeline.Name = committed;
            }
        }
        else if (_renameTargetKind == RenameTargetKind.StateMachine)
        {
            TryRenameStateMachine(stateMachines, (ushort)_renameTargetId, committed);
        }

        _renameTargetKind = RenameTargetKind.None;
        _renameTargetId = 0;
        _renameLength = 0;
        _renameNeedsFocus = false;
        _renameStartFrame = 0;
    }

    private static bool TryAddStateMachine(StateMachineDefinitionComponent.ViewProxy def, out ushort createdId)
    {
        createdId = 0;

        int count = def.MachineCount;
        if (count >= StateMachineDefinitionComponent.MaxMachines)
        {
            return false;
        }

        ushort id = def.NextMachineId;
        if (id == 0)
        {
            id = 1;
        }

        def.NextMachineId = (ushort)(id + 1);
        Span<ushort> machineIds = def.MachineIdSpan();
        Span<StringHandle> machineNames = def.MachineNameSpan();
        machineIds[count] = id;
        machineNames[count] = $"State Machine {count + 1}";
        def.MachineCount = (ushort)(count + 1);

        // Always create a default layer for new machines.
        _ = TryAddLayer(def, machineId: id, name: $"Layer 1");

        createdId = id;
        return true;
    }

    private static bool TryRenameStateMachine(StateMachineDefinitionComponent.ViewProxy def, ushort machineId, string committed)
    {
        if (machineId == 0)
        {
            return false;
        }

        int count = def.MachineCount;
        Span<ushort> ids = def.MachineIdSpan();
        Span<StringHandle> names = def.MachineNameSpan();
        for (int i = 0; i < count; i++)
        {
            if (ids[i] == machineId)
            {
                names[i] = committed;
                return true;
            }
        }

        return false;
    }

    private static bool TryAddLayer(StateMachineDefinitionComponent.ViewProxy def, ushort machineId, string name)
    {
        int layerCount = def.LayerCount;
        if (layerCount >= StateMachineDefinitionComponent.MaxLayers)
        {
            return false;
        }

        ushort layerId = def.NextLayerId;
        if (layerId == 0)
        {
            layerId = 1;
        }

        def.NextLayerId = (ushort)(layerId + 1);

        Span<ushort> layerIds = def.LayerIdSpan();
        Span<ushort> layerMachineIds = def.LayerMachineIdSpan();
        Span<StringHandle> layerNames = def.LayerNameSpan();
        Span<ushort> layerEntryTargets = def.LayerEntryTargetStateIdSpan();
        Span<ushort> layerNextStateIds = def.LayerNextStateIdSpan();
        Span<ushort> layerNextTransitionIds = def.LayerNextTransitionIdSpan();

        Span<System.Numerics.Vector2> layerEntryPos = def.LayerEntryPosSpan();
        Span<System.Numerics.Vector2> layerAnyPos = def.LayerAnyStatePosSpan();
        Span<System.Numerics.Vector2> layerExitPos = def.LayerExitPosSpan();
        Span<System.Numerics.Vector2> layerPan = def.LayerPanSpan();
        Span<float> layerZoom = def.LayerZoomSpan();

        layerIds[layerCount] = layerId;
        layerMachineIds[layerCount] = machineId;
        layerNames[layerCount] = name;
        layerEntryTargets[layerCount] = 0;
        layerNextStateIds[layerCount] = 1;
        layerNextTransitionIds[layerCount] = 1;

        layerEntryPos[layerCount] = new System.Numerics.Vector2(0f, -140f);
        layerAnyPos[layerCount] = new System.Numerics.Vector2(-180f, 160f);
        layerExitPos[layerCount] = new System.Numerics.Vector2(180f, 160f);
        layerPan[layerCount] = System.Numerics.Vector2.Zero;
        layerZoom[layerCount] = 1f;

        def.LayerCount = (ushort)(layerCount + 1);
        return true;
    }
}
