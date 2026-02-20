using System;
using Core;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;
using DerpLib.ImGui.Widgets;
using Property;
using Property.Runtime;

namespace Derp.UI;

internal static class StateMachineSelectionInspector
{
    private const int StateNameMaxChars = 64;

    private static readonly string[] BoolOpOptions =
    {
        "Is True",
        "Is False"
    };

    private static readonly string[] CompareOpOptions =
    {
        "Equals",
        "Not Equals",
        "Greater Than",
        "Less Than",
        "Greater Or Equal",
        "Less Or Equal"
    };

    private static string[] _variableOptions = Array.Empty<string>();
    private static ushort[] _variableOptionIds = Array.Empty<ushort>();
    private static int _variableOptionCount;

    private static string[] _timelineOptions = Array.Empty<string>();
    private static int[] _timelineOptionIds = Array.Empty<int>();
    private static int _timelineOptionCount;

    private static ushort _stateNameEditStateId;
    private static StringHandle _stateNameEditHandle;
    private static readonly char[] _stateNameEditBuffer = new char[StateNameMaxChars];
    private static int _stateNameEditLength;
    private static int _stateNameEditStartFrame;

    public static void Draw(UiWorkspace workspace, UiWorkspace.StateMachineInspectorSelection selection)
    {
        if (!workspace.TryGetActiveStateMachineDefinition(out var def))
        {
            InspectorCard.Begin("State Machine");
            InspectorHint.Draw("No state machines");
            InspectorCard.End();
            return;
        }

        ushort machineId = (ushort)selection.StateMachineId;
        ushort layerId = (ushort)selection.LayerId;

        if (!StateMachineDefinitionOps.TryFindMachineSlotIndexById(def, machineId, out _))
        {
            InspectorCard.Begin("State Machine");
            InspectorHint.Draw("Missing state machine");
            InspectorCard.End();
            return;
        }

        if (!StateMachineDefinitionOps.TryFindLayerSlotIndexById(def, layerId, out _))
        {
            InspectorCard.Begin("State Machine");
            InspectorHint.Draw("Missing layer");
            InspectorCard.End();
            return;
        }

        if (selection.Kind == UiWorkspace.StateMachineInspectorSelectionKind.MultiNode)
        {
            InspectorCard.Begin("State Machine");
            InspectorHint.Draw(selection.NodeCount > 1 ? $"Multiple states selected ({selection.NodeCount})" : "Multiple states selected");
            InspectorCard.End();
            return;
        }

        if (selection.Kind == UiWorkspace.StateMachineInspectorSelectionKind.Transition)
        {
            InspectorCard.Begin("Transition");

            if (TryFindTransitionSlot(def, layerId, selection.TransitionId, out int transitionSlot))
            {
                var fromKind = (StateMachineDefinitionComponent.TransitionFromKind)def.TransitionFromKindValue[transitionSlot];
                var toKind = (StateMachineDefinitionComponent.TransitionToKind)def.TransitionToKindValue[transitionSlot];
                int fromStateId = def.TransitionFromStateId[transitionSlot];
                int toStateId = def.TransitionToStateId[transitionSlot];

                InspectorHint.Draw("From");
                DrawFromEndpoint(def, layerId, fromKind, fromStateId);
                InspectorHint.Draw("To");
                DrawToEndpoint(def, layerId, toKind, toStateId);

                DrawTransitionSettings(def, transitionSlot);
                DrawTransitionConditions(workspace, def, transitionSlot);
                InspectorCard.End();
                return;
            }

            InspectorHint.Draw("Missing transition");
            InspectorCard.End();
            return;
        }

        if (selection.Kind != UiWorkspace.StateMachineInspectorSelectionKind.Node)
        {
            InspectorCard.Begin("State Machine");
            InspectorHint.Draw("No selection");
            InspectorCard.End();
            return;
        }

        var node = selection.Node;
        if (node.Kind == StateMachineGraphNodeKind.Entry)
        {
            InspectorCard.Begin("Entry");
            InspectorHint.Draw("Entry node");
            InspectorCard.End();
            return;
        }

        if (node.Kind == StateMachineGraphNodeKind.AnyState)
        {
            InspectorCard.Begin("Any State");
            InspectorHint.Draw("Any State node");
            InspectorCard.End();
            return;
        }

        if (node.Kind == StateMachineGraphNodeKind.Exit)
        {
            InspectorCard.Begin("Exit");
            InspectorHint.Draw("Exit node");
            InspectorCard.End();
            return;
        }

        if (node.Kind == StateMachineGraphNodeKind.State && TryFindStateSlot(def, layerId, node.Id, out int stateSlot))
        {
            InspectorCard.Begin("State");

            string name = def.StateName[stateSlot];
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "State";
            }

            InspectorHint.Draw(name);

            var kind = (StateMachineDefinitionComponent.StateKind)def.StateKindValue[stateSlot];
            InspectorHint.Draw(GetStateKindLabel(kind));

            var ctx = Im.Context;
            ctx.PushId(stateSlot);
            DrawStateNameRow(def, stateSlot);
            DrawStateSettings(workspace, def, stateSlot, kind);
            ctx.PopId();

            InspectorCard.End();
            return;
        }

        InspectorCard.Begin("State");
        InspectorHint.Draw("Missing state");
        InspectorCard.End();
    }

    private static void DrawTransitionSettings(StateMachineDefinitionComponent.ViewProxy def, int transitionSlot)
    {
        InspectorHint.Draw("Settings");

        Span<float> durationSpan = def.TransitionDurationSecondsSpan();
        float duration = durationSpan[transitionSlot];
        bool durationChanged = DrawFloatRow("Duration (s)", "sm_tr_duration", ref duration, minValue: 0f, maxValue: 5f);
        if (durationChanged)
        {
            durationSpan[transitionSlot] = MathF.Max(0f, duration);
        }

        Span<byte> hasExitTimeSpan = def.TransitionHasExitTimeValueSpan();
        bool hasExitTime = hasExitTimeSpan[transitionSlot] != 0;
        bool exitChanged = DrawBoolRow("Has Exit Time", "sm_tr_has_exit", ref hasExitTime);
        if (exitChanged)
        {
            hasExitTimeSpan[transitionSlot] = hasExitTime ? (byte)1 : (byte)0;
        }

        if (hasExitTime)
        {
            Span<float> exitSpan = def.TransitionExitTime01Span();
            float exit01 = exitSpan[transitionSlot];
            bool changed = DrawFloatRow("Exit Time (0-1)", "sm_tr_exit01", ref exit01, minValue: 0f, maxValue: 1f);
            if (changed)
            {
                exitSpan[transitionSlot] = Math.Clamp(exit01, 0f, 1f);
            }
        }
    }

    private static void DrawStateSettings(
        UiWorkspace workspace,
        StateMachineDefinitionComponent.ViewProxy def,
        int stateSlot,
        StateMachineDefinitionComponent.StateKind kind)
    {
        InspectorHint.Draw("Settings");

        if (kind == StateMachineDefinitionComponent.StateKind.Timeline)
        {
            DrawTimelineStateSettings(workspace, def, stateSlot);
            return;
        }

        if (kind == StateMachineDefinitionComponent.StateKind.Blend1D)
        {
            DrawBlend1DStateSettings(workspace, def, stateSlot);
            return;
        }

        InspectorHint.Draw("No state settings for this type yet");
    }

    private static void DrawStateNameRow(StateMachineDefinitionComponent.ViewProxy def, int stateSlot)
    {
        Span<StringHandle> names = def.StateNameSpan();
        StringHandle current = names[stateSlot];
        ushort stateId = def.StateId[stateSlot];

        var ctx = Im.Context;
        int widgetId = ctx.GetId("sm_state_name");
        bool isFocused = ctx.IsFocused(widgetId);

        bool selectionChanged = _stateNameEditStateId != stateId;
        bool handleChanged = _stateNameEditHandle != current;
        if (selectionChanged || (!isFocused && handleChanged))
        {
            _stateNameEditStateId = stateId;
            _stateNameEditHandle = current;
            _stateNameEditStartFrame = ctx.FrameCount;
            string currentValue = current;
            SetBufferFromString(currentValue, _stateNameEditBuffer, out _stateNameEditLength);
        }

        var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rowRect = InspectorRow.GetPaddedRect(rowRect);

        const float labelWidth = 120f;
        float inputWidth = Math.Max(120f, rowRect.Width - labelWidth);
        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text("Name".AsSpan(), rowRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        int length = _stateNameEditLength;
        bool changed = Im.TextInput("sm_state_name", _stateNameEditBuffer, ref length, _stateNameEditBuffer.Length, rowRect.X + labelWidth, rowRect.Y, inputWidth);
        _stateNameEditLength = length;

        bool nowFocused = ctx.IsFocused(widgetId);
        var input = ctx.Input;
        if (nowFocused && input.KeyEscape)
        {
            string currentValue = current;
            SetBufferFromString(currentValue, _stateNameEditBuffer, out _stateNameEditLength);
            _stateNameEditHandle = current;
            return;
        }

        bool canCommitOutsideClick = ctx.FrameCount != _stateNameEditStartFrame;
        bool commit = input.KeyEnter || (canCommitOutsideClick && !nowFocused && Im.MousePressed);
        if ((changed && commit) || (commit && !nowFocused))
        {
            string committed = new string(_stateNameEditBuffer, 0, length).Trim();
            if (committed.Length == 0)
            {
                committed = "State";
            }

            StringHandle committedHandle = committed;
            names[stateSlot] = committedHandle;
            _stateNameEditHandle = committedHandle;
        }
    }

    private static void SetBufferFromString(string value, char[] buffer, out int length)
    {
        if (string.IsNullOrEmpty(value))
        {
            length = 0;
            buffer[0] = '\0';
            return;
        }

        int copyLen = Math.Min(value.Length, buffer.Length - 1);
        value.AsSpan(0, copyLen).CopyTo(buffer);
        length = copyLen;
        buffer[length] = '\0';
    }

    private static void DrawTimelineStateSettings(UiWorkspace workspace, StateMachineDefinitionComponent.ViewProxy def, int stateSlot)
    {
        Span<int> stateTimelineId = def.StateTimelineIdSpan();
        int timelineId = stateTimelineId[stateSlot];

        if (workspace.TryGetActiveAnimationDocument(out var animations) && animations != null)
        {
            EnsureTimelineDropdownOptions(animations);
            int selectedIndex = FindTimelineOptionIndex(timelineId);
            bool changed = DrawDropdownRow("Timeline", "sm_state_timeline", _timelineOptions.AsSpan(0, _timelineOptionCount), ref selectedIndex);
            if (changed && selectedIndex >= 0 && selectedIndex < _timelineOptionCount)
            {
                stateTimelineId[stateSlot] = _timelineOptionIds[selectedIndex];
            }
        }
        else
        {
            InspectorHint.Draw("No animations available");
        }

        Span<float> playbackSpeed = def.StatePlaybackSpeedSpan();
        float speed = playbackSpeed[stateSlot];
        if (speed <= 0f)
        {
            speed = 1f;
            playbackSpeed[stateSlot] = speed;
        }

        bool speedChanged = DrawFloatRow("Speed", "sm_state_speed", ref speed, minValue: 0f, maxValue: 5f);
        if (speedChanged)
        {
            playbackSpeed[stateSlot] = MathF.Max(0f, speed);
        }
    }

    private static void DrawBlend1DStateSettings(UiWorkspace workspace, StateMachineDefinitionComponent.ViewProxy def, int stateSlot)
    {
        PrefabVariablesComponent.ViewProxy vars = default;
        bool hasVars = TryGetActivePrefabVariables(workspace, out vars);
        EnsureVariableDropdownOptions(vars);

        Span<ushort> parameter = def.StateBlendParameterVariableIdSpan();
        ushort paramId = parameter[stateSlot];

        int selectedVarIndex = FindVariableOptionIndex(paramId);
        bool paramChanged = DrawDropdownRow("Parameter", "sm_blend1d_param", _variableOptions.AsSpan(0, _variableOptionCount), ref selectedVarIndex);
        if (paramChanged && selectedVarIndex >= 0 && selectedVarIndex < _variableOptionCount)
        {
            parameter[stateSlot] = _variableOptionIds[selectedVarIndex];
        }

        InspectorHint.Draw("Motions");

        Span<ushort> childStartSpan = def.StateBlendChildStartSpan();
        Span<byte> childCountSpan = def.StateBlendChildCountSpan();
        ushort start = childStartSpan[stateSlot];
        byte count = childCountSpan[stateSlot];

        if (count == 0)
        {
            InspectorHint.Draw("No motions (no output)");
        }

        AnimationDocument? animations = null;
        if (workspace.TryGetActiveAnimationDocument(out var animDoc) && animDoc != null)
        {
            animations = animDoc;
            EnsureTimelineDropdownOptions(animDoc);
        }

        for (int i = 0; i < count; i++)
        {
            int childSlot = start + i;
            if (DrawBlend1DChildRow(workspace, def, stateSlot, childSlot, i, animations != null))
            {
                return;
            }
        }

        if (InspectorButtonRow.Draw("sm_blend1d_add", "+ Add Motion"))
        {
            int timelineId = 0;
            if (animations != null && _timelineOptionCount > 1)
            {
                timelineId = _timelineOptionIds[1];
            }

            _ = StateMachineDefinitionOps.TryAddBlend1DChild(def, stateSlot, threshold: 0f, timelineId);
        }
    }

    private static bool DrawBlend1DChildRow(
        UiWorkspace workspace,
        StateMachineDefinitionComponent.ViewProxy def,
        int stateSlot,
        int childSlot,
        int childIndex,
        bool hasAnimations)
    {
        var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rowRect = InspectorRow.GetPaddedRect(rowRect);

        var ctx = Im.Context;
        ctx.PushId(stateSlot);
        ctx.PushId(childIndex);

        float removeW = 28f;
        float labelW = 86f;
        float thresholdW = 90f;
        float spacing = Im.Style.Spacing;

        float y = rowRect.Y;
        float h = rowRect.Height;
        float textY = y + (h - Im.Style.FontSize) * 0.5f;

        Im.Text($"Motion {childIndex + 1}".AsSpan(), rowRect.X, textY, Im.Style.FontSize, Im.Style.TextSecondary);

        Span<float> thresholds = def.BlendChildThresholdSpan();
        Span<int> timelineIds = def.BlendChildTimelineIdSpan();

        float thresholdX = rowRect.X + labelW;
        float timelineX = thresholdX + thresholdW + spacing;
        float timelineW = Math.Max(120f, rowRect.Width - labelW - thresholdW - removeW - spacing * 2f);

        float th = thresholds[childSlot];
        bool thresholdChanged = ImScalarInput.DrawAt("th", thresholdX, y, thresholdW, ref th, -9999f, 9999f, "F2");
        if (thresholdChanged)
        {
            thresholds[childSlot] = th;
        }

        int timelineId = timelineIds[childSlot];
        if (hasAnimations)
        {
            int selectedIndex = FindTimelineOptionIndex(timelineId);
            bool changed = Im.Dropdown("tl", _timelineOptions.AsSpan(0, _timelineOptionCount), ref selectedIndex, timelineX, y, timelineW);
            if (changed && selectedIndex >= 0 && selectedIndex < _timelineOptionCount)
            {
                timelineIds[childSlot] = _timelineOptionIds[selectedIndex];
            }
        }
        else
        {
            Im.Text("No animations".AsSpan(), timelineX + 6f, textY, Im.Style.FontSize, Im.Style.TextSecondary);
        }

        float removeX = rowRect.X + rowRect.Width - removeW;
        if (Im.Button("-", removeX, y, removeW, h))
        {
            _ = StateMachineDefinitionOps.TryRemoveBlend1DChild(def, stateSlot, childIndex);
            ctx.PopId();
            ctx.PopId();
            return true;
        }

        ctx.PopId();
        ctx.PopId();
        return false;
    }

    private static void DrawTransitionConditions(UiWorkspace workspace, StateMachineDefinitionComponent.ViewProxy def, int transitionSlot)
    {
        InspectorHint.Draw("Conditions");

        var input = Im.Context.Input;
        bool deleteHotkey = (input.KeyDelete || input.KeyBackspace) && Im.Context.FocusId == 0 && !Im.Context.AnyActive && !Im.Context.WantCaptureKeyboard;

        PrefabVariablesComponent.ViewProxy vars;
        bool hasVars = TryGetActivePrefabVariables(workspace, out vars);
        EnsureVariableDropdownOptions(vars);

        ushort start = def.TransitionConditionStart[transitionSlot];
        byte count = def.TransitionConditionCount[transitionSlot];

        if (count == 0)
        {
            InspectorHint.Draw("No conditions (always allowed)");
        }

        for (int i = 0; i < count; i++)
        {
            int conditionSlot = start + i;
            if (DrawConditionRow(def, transitionSlot, conditionSlot, conditionIndex: i, hasVars, vars, deleteHotkey))
            {
                return;
            }
        }

        if (InspectorButtonRow.Draw("sm_tr_add_cond", "+ Add Condition"))
        {
            ushort variableId = 0;
            PropertyKind kind = PropertyKind.Bool;
            PropertyValue defaultValue = default;
            if (TryGetFirstPrefabVariable(vars, out ushort id, out PropertyKind k, out PropertyValue dv))
            {
                variableId = id;
                kind = k;
                defaultValue = dv;
            }

            var op = (kind == PropertyKind.Bool || kind == PropertyKind.Trigger)
                ? StateMachineDefinitionComponent.ConditionOp.IsTrue
                : StateMachineDefinitionComponent.ConditionOp.Equals;
            _ = StateMachineDefinitionOps.TryAddTransitionCondition(def, transitionSlot, variableId, op, defaultValue);
        }

        if (count > 0 && InspectorButtonRow.Draw("sm_tr_clear_cond", "Clear Conditions"))
        {
            StateMachineDefinitionOps.RemoveAllTransitionConditions(def, transitionSlot);
        }
    }

    private static bool DrawConditionRow(
        StateMachineDefinitionComponent.ViewProxy def,
        int transitionSlot,
        int conditionSlot,
        int conditionIndex,
        bool hasVars,
        PrefabVariablesComponent.ViewProxy vars,
        bool deleteHotkey)
    {
        var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rowRect = InspectorRow.GetPaddedRect(rowRect);

        bool rowHovered = rowRect.Contains(Im.MousePos);

        float spacing = Im.Style.Spacing;
        float buttonSize = rowRect.Height;
        float removeW = buttonSize;
        float available = MathF.Max(1f, rowRect.Width - removeW - spacing);

        float variableW = MathF.Max(120f, available * 0.48f);
        float opW = MathF.Max(110f, available * 0.26f);
        float valueW = MathF.Max(1f, available - variableW - opW - spacing * 2f);

        float x = rowRect.X;
        float y = rowRect.Y;

        var ctx = Im.Context;
        ctx.PushId(transitionSlot);
        ctx.PushId(conditionIndex);

        Span<ushort> varIds = def.ConditionVariableIdSpan();
        Span<byte> ops = def.ConditionOpValueSpan();
        Span<PropertyValue> values = def.ConditionCompareValueSpan();

        ushort variableId = varIds[conditionSlot];
        PropertyKind kind = PropertyKind.Bool;
        PropertyValue defaultValue = default;
        if (hasVars && !TryGetPrefabVariableKindAndDefault(vars, variableId, out kind, out defaultValue))
        {
            kind = PropertyKind.Bool;
        }

        int selectedVarIndex = FindVariableOptionIndex(variableId);
        bool variableChanged = Im.Dropdown("var", _variableOptions.AsSpan(0, _variableOptionCount), ref selectedVarIndex, x, y, variableW);
        if (variableChanged && selectedVarIndex >= 0 && selectedVarIndex < _variableOptionCount)
        {
            ushort nextVarId = _variableOptionIds[selectedVarIndex];
            varIds[conditionSlot] = nextVarId;
            variableId = nextVarId;

            if (hasVars && TryGetPrefabVariableKindAndDefault(vars, variableId, out kind, out defaultValue))
            {
                values[conditionSlot] = defaultValue;
                ops[conditionSlot] = kind == PropertyKind.Bool || kind == PropertyKind.Trigger
                    ? (byte)StateMachineDefinitionComponent.ConditionOp.IsTrue
                    : (byte)StateMachineDefinitionComponent.ConditionOp.Equals;
            }
        }

        var opValue = (StateMachineDefinitionComponent.ConditionOp)ops[conditionSlot];
        bool boolLike = kind == PropertyKind.Bool || kind == PropertyKind.Trigger;
        int opIndex = boolLike ? GetBoolOpIndex(opValue) : GetCompareOpIndex(opValue);
        bool opChanged = Im.Dropdown("op",
            (boolLike ? BoolOpOptions : CompareOpOptions).AsSpan(),
            ref opIndex,
            x + variableW + spacing,
            y,
            opW);
        if (opChanged)
        {
            var mapped = boolLike ? GetBoolOpByIndex(opIndex) : GetCompareOpByIndex(opIndex);
            ops[conditionSlot] = (byte)mapped;
            opValue = mapped;
        }

        bool needsValue = !boolLike;
        if (needsValue)
        {
            PropertyValue compareValue = values[conditionSlot];
            float valueX = x + variableW + spacing + opW + spacing;

            if (kind == PropertyKind.Float)
            {
                float f = compareValue.Float;
                if (ImScalarInput.DrawAt("val", valueX, y, valueW, ref f, float.MinValue, float.MaxValue, "F2"))
                {
                    values[conditionSlot] = PropertyValue.FromFloat(f);
                }
            }
            else if (kind == PropertyKind.Int)
            {
                float f = compareValue.Int;
                if (ImScalarInput.DrawAt("val", valueX, y, valueW, ref f, int.MinValue, int.MaxValue, "F0"))
                {
                    int iv = (int)MathF.Round(f);
                    values[conditionSlot] = PropertyValue.FromInt(iv);
                }
            }
            else
            {
                Im.DrawRoundedRect(valueX, y, valueW, rowRect.Height, Im.Style.CornerRadius, ImStyle.WithAlphaF(Im.Style.Surface, 0.65f));
                float textY = y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
                Im.Text("Unsupported".AsSpan(), valueX + 8f, textY, Im.Style.FontSize, Im.Style.TextSecondary);
            }
        }

        float removeX = rowRect.X + rowRect.Width - removeW;
        if (Im.Button("-", removeX, y, removeW, rowRect.Height))
        {
            _ = StateMachineDefinitionOps.TryRemoveTransitionCondition(def, transitionSlot, conditionIndex);
            ctx.PopId();
            ctx.PopId();
            return true;
        }

        if (deleteHotkey && rowHovered)
        {
            _ = StateMachineDefinitionOps.TryRemoveTransitionCondition(def, transitionSlot, conditionIndex);
            ctx.PopId();
            ctx.PopId();
            return true;
        }

        ctx.PopId();
        ctx.PopId();
        return false;
    }

    private static bool DrawFloatRow(string label, string id, ref float value, float minValue, float maxValue)
    {
        var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rowRect = InspectorRow.GetPaddedRect(rowRect);

        float labelWidth = 120f;
        float inputWidth = Math.Max(120f, rowRect.Width - labelWidth);
        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), rowRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        return ImScalarInput.DrawAt(id, rowRect.X + labelWidth, rowRect.Y, inputWidth, ref value, minValue, maxValue, "F2");
    }

    private static bool DrawBoolRow(string label, string id, ref bool value)
    {
        var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rowRect = InspectorRow.GetPaddedRect(rowRect);

        float labelWidth = 120f;
        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), rowRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        float x = rowRect.X + labelWidth;
        float y = rowRect.Y + (rowRect.Height - Im.Style.CheckboxSize) * 0.5f;
        var ctx = Im.Context;
        ctx.PushId(id);
        bool changed = Im.Checkbox("value", ref value, x, y);
        ctx.PopId();
        return changed;
    }

    private static bool DrawDropdownRow(string label, string id, ReadOnlySpan<string> options, ref int selectedIndex)
    {
        var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rowRect = InspectorRow.GetPaddedRect(rowRect);

        float labelWidth = 120f;
        float inputWidth = Math.Max(120f, rowRect.Width - labelWidth);
        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), rowRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        return Im.Dropdown(id, options, ref selectedIndex, rowRect.X + labelWidth, rowRect.Y, inputWidth);
    }

    private static bool TryGetActivePrefabVariables(UiWorkspace workspace, out PrefabVariablesComponent.ViewProxy vars)
    {
        vars = default;
        if (!workspace.TryGetActivePrefabEntity(out EntityId prefabEntity))
        {
            return false;
        }

        if (!workspace.World.TryGetComponent(prefabEntity, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) ||
            !varsAny.IsValid)
        {
            return false;
        }

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        vars = PrefabVariablesComponent.Api.FromHandle(workspace.PropertyWorld, varsHandle);
        return vars.IsAlive;
    }

    private static void EnsureVariableDropdownOptions(PrefabVariablesComponent.ViewProxy vars)
    {
        _variableOptionCount = 0;

        if (!vars.IsAlive || vars.VariableCount == 0)
        {
            EnsureVariableDropdownCapacity(1);
            _variableOptions[0] = "(no variables)";
            _variableOptionIds[0] = 0;
            _variableOptionCount = 1;
            return;
        }

        ushort count = vars.VariableCount;
        if (count > PrefabVariablesComponent.MaxVariables)
        {
            count = PrefabVariablesComponent.MaxVariables;
        }

        EnsureVariableDropdownCapacity(count);

        ReadOnlySpan<ushort> ids = vars.VariableIdReadOnlySpan();
        ReadOnlySpan<StringHandle> names = vars.NameReadOnlySpan();
        for (int i = 0; i < count; i++)
        {
            ushort id = ids[i];
            if (id == 0)
            {
                continue;
            }

            string name = names[i].IsValid ? names[i].ToString() : "Var";
            _variableOptions[_variableOptionCount] = name;
            _variableOptionIds[_variableOptionCount] = id;
            _variableOptionCount++;
        }

        if (_variableOptionCount == 0)
        {
            EnsureVariableDropdownCapacity(1);
            _variableOptions[0] = "(no variables)";
            _variableOptionIds[0] = 0;
            _variableOptionCount = 1;
        }
    }

    private static void EnsureVariableDropdownCapacity(int capacity)
    {
        if (_variableOptions.Length < capacity)
        {
            _variableOptions = new string[Math.Max(capacity, 8)];
        }

        if (_variableOptionIds.Length < capacity)
        {
            _variableOptionIds = new ushort[Math.Max(capacity, 8)];
        }
    }

    private static void EnsureTimelineDropdownOptions(AnimationDocument animations)
    {
        _timelineOptionCount = 0;

        int timelineCount = animations.Timelines.Count;
        if (timelineCount <= 0)
        {
            EnsureTimelineDropdownCapacity(1);
            _timelineOptions[0] = "(no timelines)";
            _timelineOptionIds[0] = 0;
            _timelineOptionCount = 1;
            return;
        }

        if (timelineCount > AnimationLibraryComponent.MaxTimelines)
        {
            timelineCount = AnimationLibraryComponent.MaxTimelines;
        }

        EnsureTimelineDropdownCapacity(timelineCount + 1);
        _timelineOptions[0] = "(none)";
        _timelineOptionIds[0] = 0;

        for (int i = 0; i < timelineCount; i++)
        {
            var timeline = animations.Timelines[i];
            _timelineOptions[i + 1] = string.IsNullOrWhiteSpace(timeline.Name) ? "Timeline" : timeline.Name;
            _timelineOptionIds[i + 1] = timeline.Id;
        }

        _timelineOptionCount = timelineCount + 1;
    }

    private static void EnsureTimelineDropdownCapacity(int capacity)
    {
        if (_timelineOptions.Length < capacity)
        {
            _timelineOptions = new string[Math.Max(capacity, 8)];
        }

        if (_timelineOptionIds.Length < capacity)
        {
            _timelineOptionIds = new int[Math.Max(capacity, 8)];
        }
    }

    private static int FindTimelineOptionIndex(int timelineId)
    {
        if (timelineId <= 0)
        {
            return 0;
        }

        for (int i = 0; i < _timelineOptionCount; i++)
        {
            if (_timelineOptionIds[i] == timelineId)
            {
                return i;
            }
        }

        return 0;
    }

    private static int FindVariableOptionIndex(ushort variableId)
    {
        for (int i = 0; i < _variableOptionCount; i++)
        {
            if (_variableOptionIds[i] == variableId)
            {
                return i;
            }
        }
        return 0;
    }

    private static bool TryGetFirstPrefabVariable(PrefabVariablesComponent.ViewProxy vars, out ushort variableId, out PropertyKind kind, out PropertyValue defaultValue)
    {
        variableId = 0;
        kind = default;
        defaultValue = default;
        if (!vars.IsAlive || vars.VariableCount == 0)
        {
            return false;
        }

        ushort count = vars.VariableCount;
        if (count > PrefabVariablesComponent.MaxVariables)
        {
            count = PrefabVariablesComponent.MaxVariables;
        }

        ReadOnlySpan<ushort> ids = vars.VariableIdReadOnlySpan();
        ReadOnlySpan<int> kinds = vars.KindReadOnlySpan();
        ReadOnlySpan<PropertyValue> defaults = vars.DefaultValueReadOnlySpan();
        for (int i = 0; i < count; i++)
        {
            ushort id = ids[i];
            if (id == 0)
            {
                continue;
            }

            variableId = id;
            kind = (PropertyKind)kinds[i];
            defaultValue = defaults[i];
            return true;
        }

        return false;
    }

    private static bool TryGetPrefabVariableKindAndDefault(PrefabVariablesComponent.ViewProxy vars, ushort variableId, out PropertyKind kind, out PropertyValue defaultValue)
    {
        kind = default;
        defaultValue = default;
        if (!vars.IsAlive || vars.VariableCount == 0 || variableId == 0)
        {
            return false;
        }

        ushort count = vars.VariableCount;
        if (count > PrefabVariablesComponent.MaxVariables)
        {
            count = PrefabVariablesComponent.MaxVariables;
        }

        ReadOnlySpan<ushort> ids = vars.VariableIdReadOnlySpan();
        ReadOnlySpan<int> kinds = vars.KindReadOnlySpan();
        ReadOnlySpan<PropertyValue> defaults = vars.DefaultValueReadOnlySpan();
        for (int i = 0; i < count; i++)
        {
            if (ids[i] == variableId)
            {
                kind = (PropertyKind)kinds[i];
                defaultValue = defaults[i];
                return true;
            }
        }

        return false;
    }

    private static int GetBoolOpIndex(StateMachineDefinitionComponent.ConditionOp op)
    {
        return op == StateMachineDefinitionComponent.ConditionOp.IsFalse ? 1 : 0;
    }

    private static StateMachineDefinitionComponent.ConditionOp GetBoolOpByIndex(int index)
    {
        return index == 1 ? StateMachineDefinitionComponent.ConditionOp.IsFalse : StateMachineDefinitionComponent.ConditionOp.IsTrue;
    }

    private static int GetCompareOpIndex(StateMachineDefinitionComponent.ConditionOp op)
    {
        return op switch
        {
            StateMachineDefinitionComponent.ConditionOp.NotEquals => 1,
            StateMachineDefinitionComponent.ConditionOp.GreaterThan => 2,
            StateMachineDefinitionComponent.ConditionOp.LessThan => 3,
            StateMachineDefinitionComponent.ConditionOp.GreaterOrEqual => 4,
            StateMachineDefinitionComponent.ConditionOp.LessOrEqual => 5,
            _ => 0
        };
    }

    private static StateMachineDefinitionComponent.ConditionOp GetCompareOpByIndex(int index)
    {
        return index switch
        {
            1 => StateMachineDefinitionComponent.ConditionOp.NotEquals,
            2 => StateMachineDefinitionComponent.ConditionOp.GreaterThan,
            3 => StateMachineDefinitionComponent.ConditionOp.LessThan,
            4 => StateMachineDefinitionComponent.ConditionOp.GreaterOrEqual,
            5 => StateMachineDefinitionComponent.ConditionOp.LessOrEqual,
            _ => StateMachineDefinitionComponent.ConditionOp.Equals
        };
    }

    private static string GetStateKindLabel(StateMachineDefinitionComponent.StateKind kind)
    {
        return kind switch
        {
            StateMachineDefinitionComponent.StateKind.Timeline => "Timeline State",
            StateMachineDefinitionComponent.StateKind.Blend1D => "Blend 1D State",
            StateMachineDefinitionComponent.StateKind.BlendAdditive => "Blend Additive State",
            _ => "Unknown State Kind"
        };
    }

    private static void DrawFromEndpoint(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, StateMachineDefinitionComponent.TransitionFromKind kind, int stateId)
    {
        if (kind == StateMachineDefinitionComponent.TransitionFromKind.Entry)
        {
            InspectorHint.Draw("Entry");
            return;
        }

        if (kind == StateMachineDefinitionComponent.TransitionFromKind.AnyState)
        {
            InspectorHint.Draw("Any State");
            return;
        }

        if (kind != StateMachineDefinitionComponent.TransitionFromKind.State)
        {
            InspectorHint.Draw("Unknown");
            return;
        }

        DrawStateName(def, layerId, stateId);
    }

    private static void DrawToEndpoint(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, StateMachineDefinitionComponent.TransitionToKind kind, int stateId)
    {
        if (kind == StateMachineDefinitionComponent.TransitionToKind.Exit)
        {
            InspectorHint.Draw("Exit");
            return;
        }

        if (kind != StateMachineDefinitionComponent.TransitionToKind.State)
        {
            InspectorHint.Draw("Unknown");
            return;
        }

        DrawStateName(def, layerId, stateId);
    }

    private static void DrawStateName(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, int stateId)
    {
        if (TryFindStateSlot(def, layerId, stateId, out int slot))
        {
            string name = def.StateName[slot];
            InspectorHint.Draw(string.IsNullOrWhiteSpace(name) ? "State" : name);
            return;
        }

        InspectorHint.Draw("Missing state");
    }

    private static bool TryFindStateSlot(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, int stateId, out int slot)
    {
        slot = -1;
        if (stateId <= 0)
        {
            return false;
        }

        int total = def.StateCount;
        for (int i = 0; i < total; i++)
        {
            if (def.StateLayerId[i] == layerId && def.StateId[i] == (ushort)stateId)
            {
                slot = i;
                return true;
            }
        }

        return false;
    }

    private static bool TryFindTransitionSlot(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, int transitionId, out int slot)
    {
        slot = -1;
        if (transitionId <= 0)
        {
            return false;
        }

        int total = def.TransitionCount;
        for (int i = 0; i < total; i++)
        {
            if (def.TransitionLayerId[i] == layerId && def.TransitionId[i] == (ushort)transitionId)
            {
                slot = i;
                return true;
            }
        }

        return false;
    }
}
