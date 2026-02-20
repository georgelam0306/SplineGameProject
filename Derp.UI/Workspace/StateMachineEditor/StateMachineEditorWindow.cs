using System;
using Core;
using FontAwesome.Sharp;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Widgets;

namespace Derp.UI;

internal static class StateMachineEditorWindow
{
    private const int RenameMaxChars = 64;

    private static readonly string AddIcon = ((char)IconChar.Plus).ToString();
    private static readonly string AddLayerButtonLabel = AddIcon;

    private static readonly StateMachineEditorState State = new();
    private static int _activePrefabId = -1;

    private static int _renameLayerId;
    private static readonly char[] _renameBuffer = new char[RenameMaxChars];
    private static int _renameLength;
    private static bool _renameNeedsFocus;
    private static int _renameStartFrame;

    public static void Draw(UiWorkspace workspace, StateMachineDefinitionComponent.ViewProxy def, ImRect rect)
    {
        var ctx = Im.Context;
        ctx.PushId(0x4B6A11F3);
        try
        {
            if (!workspace.TryGetActivePrefabId(out int prefabId))
            {
                Im.Text("No active prefab.", rect.X + Im.Style.Padding, rect.Y + Im.Style.Padding, Im.Style.FontSize, Im.Style.TextSecondary);
                return;
            }

            if (_activePrefabId != prefabId)
            {
                _activePrefabId = prefabId;
                _renameLayerId = 0;
                _renameLength = 0;
                _renameNeedsFocus = false;
                _renameStartFrame = 0;

                State.ActiveStateMachineId = 0;
                State.ActiveLayerIndex = 0;
                State.SelectedNodes.Clear();
                State.SelectedNodeDragStartPositions.Clear();
                State.SelectedTransitionId = 0;
                State.IsDraggingNodes = false;
                State.IsMarqueeActive = false;
                State.IsPanning = false;
                State.ConnectDrag = default;
            }

            if (!StateMachineDefinitionOps.TryEnsureSelectedMachineId(def, out ushort machineId))
            {
                Im.Text("Create a state machine.", rect.X + Im.Style.Padding, rect.Y + Im.Style.Padding, Im.Style.FontSize, Im.Style.TextSecondary);
                return;
            }

            if (State.ActiveStateMachineId != machineId)
            {
                State.ActiveStateMachineId = machineId;
                State.ActiveLayerIndex = 0;
                _renameLayerId = 0;
                _renameLength = 0;
                _renameNeedsFocus = false;
                _renameStartFrame = 0;

                State.SelectedNodes.Clear();
                State.SelectedNodeDragStartPositions.Clear();
                State.SelectedTransitionId = 0;
                State.IsDraggingNodes = false;
                State.IsMarqueeActive = false;
                State.IsPanning = false;
                State.ConnectDrag = default;
            }

            Span<int> layerSlots = stackalloc int[StateMachineDefinitionComponent.MaxLayers];
            int layerCount = StateMachineDefinitionOps.CollectLayerSlotIndicesForMachine(def, machineId, layerSlots);
            if (layerCount <= 0)
            {
                Im.Text("No layers.", rect.X + Im.Style.Padding, rect.Y + Im.Style.Padding, Im.Style.FontSize, Im.Style.TextSecondary);
                return;
            }

            DrawLayerTabs(def, machineId, layerSlots.Slice(0, layerCount), rect, out ImRect graphRect);

            int layerIndex = Math.Clamp(State.ActiveLayerIndex, 0, layerCount - 1);
            State.ActiveLayerIndex = layerIndex;
            int layerSlotIndex = layerSlots[layerIndex];
            ushort layerId = def.LayerId[layerSlotIndex];

            StateMachineGraphCanvasWidget.Draw(workspace, def, machineId, layerId, layerSlotIndex, State, graphRect);
        }
        finally
        {
            ctx.PopId();
        }
    }

    private static void DrawLayerTabs(
        StateMachineDefinitionComponent.ViewProxy def,
        ushort machineId,
        ReadOnlySpan<int> layerSlots,
        ImRect rect,
        out ImRect graphRect)
    {
        float barH = StateMachineEditorLayout.TopBarHeight;
        var barRect = new ImRect(rect.X, rect.Y, rect.Width, MathF.Min(rect.Height, barH));
        graphRect = new ImRect(rect.X, rect.Y + barRect.Height, rect.Width, MathF.Max(0f, rect.Height - barRect.Height));

        uint bg = ImStyle.WithAlphaF(Im.Style.Surface, 0.9f);
        Im.DrawRect(barRect.X, barRect.Y, barRect.Width, barRect.Height, bg);
        Im.DrawLine(barRect.X, barRect.Bottom, barRect.Right, barRect.Bottom, 1f, Im.Style.Border);

        float x = barRect.X + Im.Style.Padding;
        float y = barRect.Y + (barRect.Height - StateMachineEditorLayout.TabHeight) * 0.5f;

        HandleLayerRenameCommitOrCancel(def);

        for (int i = 0; i < layerSlots.Length; i++)
        {
            int layerSlot = layerSlots[i];
            ushort layerId = def.LayerId[layerSlot];
            StringHandle layerNameHandle = def.LayerName[layerSlot];
            string layerName = layerNameHandle;
            float tabW = 96f;
            var tabRect = new ImRect(x, y, tabW, StateMachineEditorLayout.TabHeight);
            bool selected = i == State.ActiveLayerIndex;

            uint tabBg = selected ? Im.Style.Primary : ImStyle.WithAlphaF(Im.Style.Background, 0.6f);
            Im.DrawRoundedRect(tabRect.X, tabRect.Y, tabRect.Width, tabRect.Height, Im.Style.CornerRadius, tabBg);
            Im.DrawRoundedRectStroke(tabRect.X, tabRect.Y, tabRect.Width, tabRect.Height, Im.Style.CornerRadius, Im.Style.Border, Im.Style.BorderWidth);

            uint textColor = selected ? 0xFFFFFFFF : Im.Style.TextPrimary;
            bool hovered = tabRect.Contains(Im.MousePos);
            bool doubleClick = hovered && Im.Context.Input.IsDoubleClick && Im.MousePressed;
            if (doubleClick)
            {
                State.ActiveLayerIndex = i;
                BeginLayerRename(layerId, layerName);
            }
            else if (hovered && Im.MousePressed && _renameLayerId == 0)
            {
                State.ActiveLayerIndex = i;
                State.SelectedNodes.Clear();
                State.SelectedNodeDragStartPositions.Clear();
                State.SelectedTransitionId = 0;
                State.IsDraggingNodes = false;
                State.IsMarqueeActive = false;
                State.ConnectDrag = default;
            }

            float textX = tabRect.X + StateMachineEditorLayout.TabPaddingX;
            float textY = tabRect.Y + (tabRect.Height - Im.Style.FontSize) * 0.5f;
            if (_renameLayerId == layerId)
            {
                DrawInlineRenameInput(textX, tabRect, textColor);
            }
            else
            {
                Im.Text(layerName.AsSpan(), textX, textY, Im.Style.FontSize, textColor);
            }

            x += tabW + StateMachineEditorLayout.TabGap;
        }

        float addY = barRect.Y + (barRect.Height - Im.Style.MinButtonHeight) * 0.5f;
        float addX = x;
        float maxAddX = barRect.Right - Im.Style.Padding - StateMachineEditorLayout.AddButtonWidth;
        if (addX > maxAddX)
        {
            addX = maxAddX;
        }

        var addRect = new ImRect(addX, addY, StateMachineEditorLayout.AddButtonWidth, Im.Style.MinButtonHeight);
        var ctx = Im.Context;
        ctx.PushId("sm_layer_add");
        if (Im.Button(AddLayerButtonLabel, addRect.X, addRect.Y, addRect.Width, addRect.Height))
        {
            int count = layerSlots.Length + 1;
            StateMachineDefinitionOps.TryAddLayer(def, machineId, $"Layer {count}");
        }
        ctx.PopId();
    }

    private static void BeginLayerRename(ushort layerId, string layerName)
    {
        _renameLayerId = layerId;
        _renameStartFrame = Im.Context.FrameCount;
        _renameLength = 0;

        string current = layerName ?? string.Empty;
        if (current.Length > 0)
        {
            int copyLen = Math.Min(current.Length, RenameMaxChars - 1);
            current.AsSpan(0, copyLen).CopyTo(_renameBuffer);
            _renameLength = copyLen;
        }

        _renameNeedsFocus = true;
    }

    private static void DrawInlineRenameInput(float textX, ImRect tabRect, uint textColor)
    {
        float maxWidth = MathF.Max(20f, tabRect.Width - StateMachineEditorLayout.TabPaddingX * 2f);

        var ctx = Im.Context;
        ctx.PushId(_renameLayerId);
        int widgetId = ctx.GetId("sm_layer_rename");

        if (_renameNeedsFocus)
        {
            ImTextArea.ClearState(widgetId);
            ctx.RequestFocus(widgetId);
            ctx.SetActive(widgetId);
            ctx.ResetCaretBlink();
            _renameNeedsFocus = false;
        }

        bool changed = ImTextArea.DrawAt(
            "sm_layer_rename",
            _renameBuffer,
            ref _renameLength,
            RenameMaxChars,
            textX,
            tabRect.Y,
            maxWidth,
            tabRect.Height,
            wordWrap: false,
            fontSizePx: Im.Style.FontSize,
            flags: ImTextArea.ImTextAreaFlags.NoBackground | ImTextArea.ImTextAreaFlags.NoBorder | ImTextArea.ImTextAreaFlags.NoRounding | ImTextArea.ImTextAreaFlags.SingleLine,
            lineHeightPx: Im.Style.FontSize,
            letterSpacingPx: 0f,
            alignX: 0,
            alignY: 1,
            textColor: textColor);

        ctx.PopId();
        _ = changed;
    }

    private static void HandleLayerRenameCommitOrCancel(StateMachineDefinitionComponent.ViewProxy def)
    {
        if (_renameLayerId == 0)
        {
            return;
        }

        var ctx = Im.Context;
        ctx.PushId(_renameLayerId);
        int widgetId = ctx.GetId("sm_layer_rename");
        bool focused = ctx.IsFocused(widgetId);
        ctx.PopId();

        var input = ctx.Input;
        if (input.KeyEscape)
        {
            _renameLayerId = 0;
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
        StateMachineDefinitionOps.TryRenameLayer(def, (ushort)_renameLayerId, committed);

        _renameLayerId = 0;
        _renameLength = 0;
        _renameNeedsFocus = false;
        _renameStartFrame = 0;
    }
}
