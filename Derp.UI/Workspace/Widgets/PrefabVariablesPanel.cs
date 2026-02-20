using System;
using System.Collections.Generic;
using System.Numerics;
using Core;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;
using DerpLib.ImGui.Widgets;
using Pooled.Runtime;
using Property;
using Property.Runtime;

namespace Derp.UI;

internal static class PrefabVariablesPanel
{
    private const int RenameMaxChars = 64;
    private const int StringBufferSize = 256;

    private static readonly string[] VariableKindOptions = new[]
    {
        "Float",
        "Int",
        "Bool",
        "Trigger",
        "Vec2",
        "Vec3",
        "Vec4",
        "Color",
        "String",
        "Fixed64",
        "Fixed64 Vec2",
        "Fixed64 Vec3",
        "Prefab",
        "Shape",
        "List"
    };

    private static readonly PropertyKind[] VariableKindValues = new[]
    {
        PropertyKind.Float,
        PropertyKind.Int,
        PropertyKind.Bool,
        PropertyKind.Trigger,
        PropertyKind.Vec2,
        PropertyKind.Vec3,
        PropertyKind.Vec4,
        PropertyKind.Color32,
        PropertyKind.StringHandle,
        PropertyKind.Fixed64,
        PropertyKind.Fixed64Vec2,
        PropertyKind.Fixed64Vec3,
        PropertyKind.PrefabRef,
        PropertyKind.ShapeRef,
        PropertyKind.List
    };

    private sealed class TextEditState
    {
        public char[] Buffer = Array.Empty<char>();
        public int Length;
        public StringHandle Handle;
    }

    private static readonly Dictionary<ulong, TextEditState> TextStates = new();

    private static uint _renameOwnerStableId;
    private static ushort _renameVariableId;
    private static readonly char[] _renameBuffer = new char[RenameMaxChars];
    private static int _renameLength;
    private static bool _renameNeedsFocus;
    private static int _renameStartFrame;

    private static string[] _entityDropdownOptions = new string[256];
    private static uint[] _entityDropdownStableIds = new uint[256];
    private static EntityId[] _entityTraversalScratch = new EntityId[512];

    public static void DrawForPrefab(UiWorkspace workspace, EntityId prefabEntity)
    {
        if (prefabEntity.IsNull || workspace.World.GetNodeType(prefabEntity) != UiNodeType.Prefab)
        {
            return;
        }

        if (!workspace.World.TryGetComponent(prefabEntity, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) ||
            varsAny.IsNull)
        {
            if (!workspace.Commands.TryEnsurePrefabVariablesComponent(prefabEntity, out varsAny))
            {
                return;
            }
        }

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        var vars = PrefabVariablesComponent.Api.FromHandle(workspace.PropertyWorld, varsHandle);
        if (!vars.IsAlive)
        {
            return;
        }

        InspectorCard.Begin("Variables");
        DrawHeaderRow(workspace, prefabEntity, vars);
        DrawVariableList(workspace, prefabEntity, vars, editInstance: false, defaultValuesSource: default, instance: default, instanceOverrideMask: 0);
        HandleRenameCommitOrCancel(workspace);
        InspectorCard.End();
    }

    public static void DrawForInstance(UiWorkspace workspace, EntityId instanceEntity)
    {
        if (instanceEntity.IsNull || workspace.World.GetNodeType(instanceEntity) != UiNodeType.PrefabInstance)
        {
            return;
        }

        if (!workspace.World.TryGetComponent(instanceEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) ||
            instanceAny.IsNull)
        {
            return;
        }

        var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
        var instance = PrefabInstanceComponent.Api.FromHandle(workspace.PropertyWorld, instanceHandle);
        if (!instance.IsAlive)
        {
            return;
        }

        uint sourcePrefabStableId = instance.SourcePrefabStableId;
        if (sourcePrefabStableId == 0)
        {
            InspectorCard.Begin("Prefab Instance");
            InspectorHint.Draw("Missing source prefab.");
            InspectorCard.End();
            return;
        }

        EntityId sourcePrefabEntity = workspace.World.GetEntityByStableId(sourcePrefabStableId);
        if (sourcePrefabEntity.IsNull || workspace.World.GetNodeType(sourcePrefabEntity) != UiNodeType.Prefab)
        {
            InspectorCard.Begin("Prefab Instance");
            InspectorHint.Draw("Invalid source prefab.");
            InspectorCard.End();
            return;
        }

        if (!workspace.World.TryGetComponent(sourcePrefabEntity, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) ||
            varsAny.IsNull)
        {
            InspectorCard.Begin("Prefab Instance");
            InspectorHint.Draw("Source prefab has no variables.");
            InspectorCard.End();
            return;
        }

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        var vars = PrefabVariablesComponent.Api.FromHandle(workspace.PropertyWorld, varsHandle);
        if (!vars.IsAlive || vars.VariableCount == 0)
        {
            InspectorCard.Begin("Prefab Instance");
            InspectorHint.Draw("No variables.");
            InspectorCard.End();
            return;
        }

        InspectorCard.Begin("Variables");
        DrawVariableList(workspace, instanceEntity, vars, editInstance: true, defaultValuesSource: vars, instance: instance, instanceOverrideMask: instance.OverrideMask);
        InspectorCard.End();
    }

    private static void DrawHeaderRow(UiWorkspace workspace, EntityId prefabEntity, PrefabVariablesComponent.ViewProxy vars)
    {
        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = InspectorRow.GetPaddedRect(rect);

        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text("Prefab Variables".AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextSecondary);

        float buttonWidth = 28f;
        var buttonRect = new ImRect(rect.Right - buttonWidth, rect.Y, buttonWidth, rect.Height);
        if (Im.Button("+", buttonRect.X, buttonRect.Y, buttonRect.Width, buttonRect.Height))
        {
            float separatorHeight = 9f;
            float estimatedHeight = ImContextMenu.ItemHeight * (VariableKindOptions.Length + 1) + separatorHeight;
            float openX = buttonRect.X;
            float openY = buttonRect.Bottom;
            var viewport = Im.CurrentViewport;
            if (viewport != null)
            {
                const float viewportMargin = 6f;
                if (openY + estimatedHeight > viewport.Size.Y - viewportMargin)
                {
                    openY = buttonRect.Y - estimatedHeight;
                }
            }

            ImContextMenu.OpenAt("prefab_var_add_menu", openX, openY);
        }

        if (ImContextMenu.Begin("prefab_var_add_menu"))
        {
            ImContextMenu.ItemDisabled("Add Variable");
            ImContextMenu.Separator();
            for (int i = 0; i < VariableKindOptions.Length && i < VariableKindValues.Length; i++)
            {
                if (ImContextMenu.Item(VariableKindOptions[i]))
                {
                    workspace.Commands.AddPrefabVariable(prefabEntity, VariableKindValues[i]);
                }
            }
            ImContextMenu.End();
        }

        ImLayout.Space(4f);
        _ = vars;
    }

    private static void DrawVariableList(
        UiWorkspace workspace,
        EntityId ownerEntity,
        PrefabVariablesComponent.ViewProxy vars,
        bool editInstance,
        PrefabVariablesComponent.ViewProxy defaultValuesSource,
        PrefabInstanceComponent.ViewProxy instance,
        ulong instanceOverrideMask)
    {
        ushort variableCount = vars.VariableCount;
        if (variableCount == 0)
        {
            InspectorHint.Draw("No variables");
            return;
        }

        if (variableCount > PrefabVariablesComponent.MaxVariables)
        {
            variableCount = (ushort)PrefabVariablesComponent.MaxVariables;
        }

        ReadOnlySpan<ushort> variableId = vars.VariableIdReadOnlySpan();
        Span<StringHandle> name = vars.NameSpan();
        Span<int> kind = vars.KindSpan();
        Span<PropertyValue> defaultValue = vars.DefaultValueSpan();

        Span<ushort> instanceVarId = default;
        Span<PropertyValue> instanceValue = default;
        ushort instanceCount = 0;
        if (editInstance)
        {
            instanceVarId = instance.VariableIdSpan();
            instanceValue = instance.ValueSpan();
            instanceCount = instance.ValueCount;
        }

        for (int index = 0; index < variableCount; index++)
        {
            ushort id = variableId[index];
            if (id == 0)
            {
                continue;
            }

            DrawVariableEntry(
                workspace,
                ownerEntity,
                vars,
                index,
                id,
                ref name[index],
                kind[index],
                ref defaultValue[index],
                editInstance,
                instanceVarId,
                instanceValue,
                instanceCount,
                defaultValuesSource,
                instanceOverrideMask);

            ImLayout.Space(6f);
        }
    }

    private static void DrawVariableEntry(
        UiWorkspace workspace,
        EntityId ownerEntity,
        PrefabVariablesComponent.ViewProxy vars,
        int variableIndex,
        ushort variableId,
        ref StringHandle name,
        int kindValue,
        ref PropertyValue defaultValue,
        bool editInstance,
        Span<ushort> instanceVariableId,
        Span<PropertyValue> instanceValue,
        ushort instanceCount,
        PrefabVariablesComponent.ViewProxy defaultValuesSource,
        ulong instanceOverrideMask)
    {
        var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rowRect = InspectorRow.GetPaddedRect(rowRect);

        uint stableId = workspace.World.GetStableId(ownerEntity);
        int ownerImId = stableId != 0 ? unchecked((int)stableId) : ownerEntity.Value;
        Im.Context.PushId(ownerImId);
        Im.Context.PushId(variableId);

        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        float labelWidth = Math.Clamp(rowRect.Width * 0.45f, 90f, 180f);
        var nameRect = new ImRect(rowRect.X, rowRect.Y, labelWidth, rowRect.Height);
        var valueRect = new ImRect(nameRect.Right + Im.Style.Spacing, rowRect.Y, Math.Max(40f, rowRect.Right - nameRect.Right - Im.Style.Spacing), rowRect.Height);

        if (!editInstance)
        {
            bool rowHovered = rowRect.Contains(Im.MousePos);
            if (rowHovered && Im.Context.Input.MouseRightPressed)
            {
                ImContextMenu.Open("prefab_var_menu");
            }
            if (ImContextMenu.Begin("prefab_var_menu"))
            {
                ImContextMenu.ItemDisabled("Variable");
                ImContextMenu.Separator();
                if (ImContextMenu.Item("Delete"))
                {
                    workspace.Commands.RemovePrefabVariable(ownerEntity, variableId);
                }
                ImContextMenu.End();
            }
        }

        if (editInstance)
        {
            string label = name.IsValid ? name.ToString() : $"Var {variableId}";
            Im.Text(label.AsSpan(), nameRect.X, textY, Im.Style.FontSize, Im.Style.TextSecondary);
        }
        else
        {
            bool hovered = nameRect.Contains(Im.MousePos);
            if (hovered && Im.Context.Input.IsDoubleClick)
            {
                BeginInlineRename(workspace, ownerEntity, variableId, currentLabel: name.IsValid ? name.ToString() : $"Var {variableId}");
            }

            if (_renameOwnerStableId == stableId && _renameVariableId == variableId)
            {
                DrawInlineRenameInput(workspace, ownerEntity, variableId, nameRect, textColor: Im.Style.TextPrimary);
            }
            else
            {
                string label = name.IsValid ? name.ToString() : $"Var {variableId}";
                Im.Text(label.AsSpan(), nameRect.X, textY, Im.Style.FontSize, Im.Style.TextSecondary);
            }
        }

        const float keyIconWidth = 18f;
        PropertyKind kind = (PropertyKind)kindValue;

        if (kind == PropertyKind.List)
        {
            if (!editInstance)
            {
                DrawListVariableEditorForPrefab(workspace, ownerEntity, variableId, ref defaultValue, valueRect);
            }
            else
            {
                var keyRect = new ImRect(valueRect.Right - keyIconWidth, valueRect.Y, keyIconWidth, valueRect.Height);
                if (workspace.Commands.TryGetKeyablePrefabInstanceVariableState(ownerEntity, variableId, kind, out bool hasTrack, out bool hasKeyAtPlayhead))
                {
                    DrawKeyIconDiamond(keyRect, filled: hasKeyAtPlayhead, highlighted: hasTrack);
                    if (keyRect.Contains(Im.MousePos) && Im.MousePressed)
                    {
                        workspace.Commands.AddPrefabInstanceVariableKeyAtPlayhead(ownerEntity, variableId, kind);
                    }
                }

                int instanceValueIndex = FindInstanceValueIndex(instanceVariableId, instanceCount, variableId);
                bool overridden = instanceValueIndex >= 0 && (instanceOverrideMask & (1UL << instanceValueIndex)) != 0;
                PropertyValue value = overridden
                    ? instanceValue[instanceValueIndex]
                    : defaultValuesSource.DefaultValueReadOnlySpan()[variableIndex];

                DrawListVariableEditorForInstance(
                    workspace,
                    ownerEntity,
                    variableId,
                    ref value,
                    overridden,
                    valueRect,
                    keyIconWidth);
            }

            Im.Context.PopId();
            Im.Context.PopId();
            _ = vars;
            return;
        }

        if (!editInstance)
        {
            DrawVariableDefaultEditor(workspace, ownerEntity, variableId, kind, valueRect.X, valueRect.Y, valueRect.Width, ref defaultValue, isInstance: false);
        }
        else
        {
            var keyRect = new ImRect(valueRect.Right - keyIconWidth, valueRect.Y, keyIconWidth, valueRect.Height);
            if (workspace.Commands.TryGetKeyablePrefabInstanceVariableState(ownerEntity, variableId, kind, out bool hasTrack, out bool hasKeyAtPlayhead))
            {
                DrawKeyIconDiamond(keyRect, filled: hasKeyAtPlayhead, highlighted: hasTrack);
                if (keyRect.Contains(Im.MousePos) && Im.MousePressed)
                {
                    workspace.Commands.AddPrefabInstanceVariableKeyAtPlayhead(ownerEntity, variableId, kind);
                }
            }

            int instanceValueIndex = FindInstanceValueIndex(instanceVariableId, instanceCount, variableId);
            bool overridden = instanceValueIndex >= 0 && (instanceOverrideMask & (1UL << instanceValueIndex)) != 0;
            PropertyValue value = overridden
                ? instanceValue[instanceValueIndex]
                : defaultValuesSource.DefaultValueReadOnlySpan()[variableIndex];

            bool changedValue = DrawVariableDefaultEditor(workspace, ownerEntity, variableId, kind, valueRect.X, valueRect.Y, Math.Max(1f, valueRect.Width - keyIconWidth), ref value, isInstance: true);
            if (changedValue)
            {
                if (!workspace.World.HasComponent(ownerEntity, ListGeneratedComponent.Api.PoolIdConst))
                {
                    int widgetId = Im.Context.GetId("var_value");
                    bool isEditing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);
                    workspace.Commands.SetPrefabInstanceVariableValue(widgetId, isEditing, ownerEntity, variableId, kind, value);
                }
            }
        }

        Im.Context.PopId();
        Im.Context.PopId();

        _ = vars;
    }

    private static void BeginInlineRename(UiWorkspace workspace, EntityId ownerEntity, ushort variableId, string currentLabel)
    {
        uint stableId = workspace.World.GetStableId(ownerEntity);
        if (stableId == 0 || variableId == 0)
        {
            return;
        }

        _renameOwnerStableId = stableId;
        _renameVariableId = variableId;
        _renameStartFrame = Im.Context.FrameCount;
        _renameLength = 0;
        if (!string.IsNullOrEmpty(currentLabel))
        {
            int copyLen = Math.Min(currentLabel.Length, RenameMaxChars - 1);
            currentLabel.AsSpan(0, copyLen).CopyTo(_renameBuffer);
            _renameLength = copyLen;
        }
        _renameNeedsFocus = true;
    }

    private static void DrawInlineRenameInput(UiWorkspace workspace, EntityId ownerEntity, ushort variableId, ImRect rect, uint textColor)
    {
        uint stableId = workspace.World.GetStableId(ownerEntity);
        if (stableId == 0 || stableId != _renameOwnerStableId || variableId != _renameVariableId)
        {
            return;
        }

        var ctx = Im.Context;
        int widgetId = ctx.GetId("prefab_var_rename");

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
        Im.DrawRoundedRect(rect.X - 2f, rect.Y + 2f, rect.Width + 4f, rect.Height - 4f, 4f, bg);
        Im.DrawRoundedRectStroke(rect.X - 2f, rect.Y + 2f, rect.Width + 4f, rect.Height - 4f, 4f, border, 1.25f);

        ref var style = ref Im.Style;
        float savedPadding = style.Padding;
        style.Padding = 0f;

        _ = ImTextArea.DrawAt(
            "prefab_var_rename",
            _renameBuffer,
            ref _renameLength,
            RenameMaxChars,
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            wordWrap: false,
            fontSizePx: style.FontSize,
            flags: ImTextArea.ImTextAreaFlags.NoBackground | ImTextArea.ImTextAreaFlags.NoBorder | ImTextArea.ImTextAreaFlags.NoRounding | ImTextArea.ImTextAreaFlags.SingleLine,
            lineHeightPx: style.FontSize,
            letterSpacingPx: 0f,
            alignX: 0,
            alignY: 1,
            textColor: textColor);

        style.Padding = savedPadding;

    }

    private static void HandleRenameCommitOrCancel(UiWorkspace workspace)
    {
        if (_renameOwnerStableId == 0 || _renameVariableId == 0)
        {
            return;
        }

        var input = Im.Context.Input;
        if (input.KeyEscape)
        {
            _renameOwnerStableId = 0;
            _renameVariableId = 0;
            _renameLength = 0;
            _renameNeedsFocus = false;
            _renameStartFrame = 0;
            return;
        }

        var ctx = Im.Context;
        ctx.PushId(unchecked((int)_renameOwnerStableId));
        ctx.PushId(_renameVariableId);
        int widgetId = ctx.GetId("prefab_var_rename");
        bool focused = ctx.IsFocused(widgetId);
        ctx.PopId();
        ctx.PopId();

        bool canCommitOutsideClick = Im.Context.FrameCount != _renameStartFrame;
        bool commit = input.KeyEnter || (canCommitOutsideClick && !focused && Im.MousePressed);
        if (!commit)
        {
            return;
        }

        EntityId ownerEntity = workspace.World.GetEntityByStableId(_renameOwnerStableId);
        if (!ownerEntity.IsNull)
        {
            string name = _renameLength <= 0 ? string.Empty : new string(_renameBuffer, 0, _renameLength);
            workspace.Commands.SetPrefabVariableName(widgetId, isEditing: false, ownerEntity, _renameVariableId, (StringHandle)name);
        }

        _renameOwnerStableId = 0;
        _renameVariableId = 0;
        _renameLength = 0;
        _renameNeedsFocus = false;
        _renameStartFrame = 0;
    }

    private static void DrawKeyIconDiamond(ImRect rect, bool filled, bool highlighted)
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

    private static bool DrawVariableDefaultEditor(
        UiWorkspace workspace,
        EntityId ownerEntity,
        ushort variableId,
        PropertyKind kind,
        float x,
        float y,
        float width,
        ref PropertyValue value,
        bool isInstance)
    {
        string widgetKey = isInstance ? "var_value" : "var_default";

        switch (kind)
        {
            case PropertyKind.Float:
            {
                float v = value.Float;
                bool changed = ImScalarInput.DrawAt(widgetKey, x, y, width, ref v, float.MinValue, float.MaxValue, "F2");
                if (changed)
                {
                    value = PropertyValue.FromFloat(v);
                    if (!isInstance)
                    {
                        int widgetId = Im.Context.GetId(widgetKey);
                        bool editing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);
                        workspace.Commands.SetPrefabVariableDefault(widgetId, editing, ownerEntity, variableId, kind, value);
                    }
                }
                return changed;
            }
            case PropertyKind.Int:
            {
                float floatValue = value.Int;
                bool changed = ImScalarInput.DrawAt(widgetKey, x, y, width, ref floatValue, int.MinValue, int.MaxValue, "F0");
                if (changed)
                {
                    int intValue = (int)MathF.Round(floatValue);
                    value = PropertyValue.FromInt(intValue);
                    if (!isInstance)
                    {
                        int widgetId = Im.Context.GetId(widgetKey);
                        bool editing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);
                        workspace.Commands.SetPrefabVariableDefault(widgetId, editing, ownerEntity, variableId, kind, value);
                    }
                }
                return changed;
            }
            case PropertyKind.Bool:
            {
                bool v = value.Bool;
                var checkboxRect = new ImRect(x, y, width, Im.Style.MinButtonHeight);
                Im.DrawRoundedRect(checkboxRect.X, checkboxRect.Y, checkboxRect.Width, checkboxRect.Height, Im.Style.CornerRadius, Im.Style.Surface);
                Im.DrawRoundedRectStroke(checkboxRect.X, checkboxRect.Y, checkboxRect.Width, checkboxRect.Height, Im.Style.CornerRadius, Im.Style.Border, Im.Style.BorderWidth);
                float checkboxX = checkboxRect.X + Im.Style.Padding;
                float checkboxY = checkboxRect.Y + (checkboxRect.Height - Im.Style.CheckboxSize) * 0.5f;
                bool changed = Im.Checkbox(widgetKey, ref v, checkboxX, checkboxY);
                if (changed)
                {
                    value = PropertyValue.FromBool(v);
                    if (!isInstance)
                    {
                        int widgetId = Im.Context.GetId(widgetKey);
                        workspace.Commands.SetPrefabVariableDefault(widgetId, isEditing: false, ownerEntity, variableId, kind, value);
                    }
                }
                return changed;
            }
            case PropertyKind.Trigger:
            {
                // Triggers are runtime pulses; keep defaults pinned false and don't expose instance overrides here.
                value = PropertyValue.FromBool(false);

                var checkboxRect = new ImRect(x, y, width, Im.Style.MinButtonHeight);
                Im.DrawRoundedRect(checkboxRect.X, checkboxRect.Y, checkboxRect.Width, checkboxRect.Height, Im.Style.CornerRadius, Im.Style.Surface);
                Im.DrawRoundedRectStroke(checkboxRect.X, checkboxRect.Y, checkboxRect.Width, checkboxRect.Height, Im.Style.CornerRadius, Im.Style.Border, Im.Style.BorderWidth);
                float checkboxX = checkboxRect.X + Im.Style.Padding;
                float checkboxY = checkboxRect.Y + (checkboxRect.Height - Im.Style.CheckboxSize) * 0.5f;
                var checkRect = new ImRect(checkboxX, checkboxY, Im.Style.CheckboxSize, Im.Style.CheckboxSize);
                Im.DrawRoundedRectStroke(checkRect.X, checkRect.Y, checkRect.Width, checkRect.Height, 3f, Im.Style.Border, Im.Style.BorderWidth);
                return false;
            }
            case PropertyKind.Vec2:
            {
                Vector2 v = value.Vec2;
                bool changed = ImVectorInput.DrawAt(widgetKey, x, y, width, ref v, float.MinValue, float.MaxValue, "F2");
                if (changed)
                {
                    value = PropertyValue.FromVec2(v);
                    if (!isInstance)
                    {
                        int widgetId = Im.Context.GetId(widgetKey);
                        bool editing = Im.Context.AnyActive;
                        workspace.Commands.SetPrefabVariableDefault(widgetId, editing, ownerEntity, variableId, kind, value);
                    }
                }
                return changed;
            }
            case PropertyKind.Vec3:
            {
                Vector3 v = value.Vec3;
                bool changed = ImVectorInput.DrawAt(widgetKey, x, y, width, ref v, float.MinValue, float.MaxValue, "F2");
                if (changed)
                {
                    value = PropertyValue.FromVec3(v);
                    if (!isInstance)
                    {
                        int widgetId = Im.Context.GetId(widgetKey);
                        bool editing = Im.Context.AnyActive;
                        workspace.Commands.SetPrefabVariableDefault(widgetId, editing, ownerEntity, variableId, kind, value);
                    }
                }
                return changed;
            }
            case PropertyKind.Vec4:
            {
                Vector4 v = value.Vec4;
                bool changed = ImVectorInput.DrawAt(widgetKey, x, y, width, ref v, float.MinValue, float.MaxValue, "F2");
                if (changed)
                {
                    value = PropertyValue.FromVec4(v);
                    if (!isInstance)
                    {
                        int widgetId = Im.Context.GetId(widgetKey);
                        bool editing = Im.Context.AnyActive;
                        workspace.Commands.SetPrefabVariableDefault(widgetId, editing, ownerEntity, variableId, kind, value);
                    }
                }
                return changed;
            }
            case PropertyKind.Color32:
            {
                var rect = new ImRect(x, y, width, Im.Style.MinButtonHeight);
                bool hovered = rect.Contains(Im.MousePos);
                uint bg = hovered ? ImStyle.Lerp(Im.Style.Surface, 0xFF000000, 0.16f) : Im.Style.Surface;
                Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, bg);
                Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, Im.Style.Border, Im.Style.BorderWidth);

                Color32 c = value.Color32;
                uint argb = UiColor32.ToArgb(c);

                float swatchSize = rect.Height - Im.Style.Padding * 2f;
                var swatchRect = new ImRect(rect.X + Im.Style.Padding, rect.Y + Im.Style.Padding, swatchSize, swatchSize);
                Im.DrawRect(swatchRect.X, swatchRect.Y, swatchRect.Width, swatchRect.Height, argb);
                Im.DrawRoundedRectStroke(swatchRect.X, swatchRect.Y, swatchRect.Width, swatchRect.Height, 2f, Im.Style.Border, 1f);

                Span<char> hex = stackalloc char[10];
                hex[0] = '#';
                WriteHexByte(hex.Slice(1, 2), c.R);
                WriteHexByte(hex.Slice(3, 2), c.G);
                WriteHexByte(hex.Slice(5, 2), c.B);
                WriteHexByte(hex.Slice(7, 2), c.A);
                float textX = swatchRect.Right + Im.Style.Padding;
                float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
                Im.Text(hex[..9], textX, textY, Im.Style.FontSize, Im.Style.TextSecondary);

                if (hovered && Im.MousePressed)
                {
                    workspace.PropertyInspector.OpenPrefabVariableColorPopover(ownerEntity, variableId, editInstance: isInstance, rect.Offset(Im.CurrentTranslation));
                }

                return false;
            }
            case PropertyKind.StringHandle:
            {
                uint stableId = workspace.World.GetStableId(ownerEntity);
                ulong stateKey = MakeTextStateKey(stableId, variableId, field: isInstance ? 3u : 2u);
                TextEditState state = GetOrCreateTextState(stateKey, StringBufferSize);

                StringHandle current = value.StringHandle;
                int widgetId = Im.Context.GetId(widgetKey);
                bool wasFocused = Im.Context.IsFocused(widgetId);
                if (!wasFocused && state.Handle != current)
                {
                    SetTextBufferFromHandle(state, current);
                }

                int length = state.Length;
                bool changed = Im.TextInput(widgetKey, state.Buffer, ref length, state.Buffer.Length, x, y, width);
                if (changed)
                {
                    StringHandle newHandle = new string(state.Buffer, 0, length);
                    value = PropertyValue.FromStringHandle(newHandle);
                    state.Handle = newHandle;
                    if (!isInstance)
                    {
                        bool editing = Im.Context.IsFocused(widgetId);
                        workspace.Commands.SetPrefabVariableDefault(widgetId, editing, ownerEntity, variableId, kind, value);
                    }
                }
                state.Length = length;
                return changed;
            }
            case PropertyKind.Fixed64:
            {
                float v = value.Fixed64.ToFloat();
                bool changed = ImScalarInput.DrawAt(widgetKey, x, y, width, ref v, float.MinValue, float.MaxValue, "F3");
                if (changed)
                {
                    value = PropertyValue.FromFixed64(FixedMath.Fixed64.FromFloat(v));
                    if (!isInstance)
                    {
                        int widgetId = Im.Context.GetId(widgetKey);
                        bool editing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);
                        workspace.Commands.SetPrefabVariableDefault(widgetId, editing, ownerEntity, variableId, kind, value);
                    }
                }
                return changed;
            }
            case PropertyKind.Fixed64Vec2:
            {
                var fixedValue = value.Fixed64Vec2;
                Vector2 v = new Vector2(fixedValue.X.ToFloat(), fixedValue.Y.ToFloat());
                bool changed = ImVectorInput.DrawAt(widgetKey, x, y, width, ref v, float.MinValue, float.MaxValue, "F3");
                if (changed)
                {
                    value = PropertyValue.FromFixed64Vec2(FixedMath.Fixed64Vec2.FromFloat(v.X, v.Y));
                    if (!isInstance)
                    {
                        int widgetId = Im.Context.GetId(widgetKey);
                        bool editing = Im.Context.AnyActive;
                        workspace.Commands.SetPrefabVariableDefault(widgetId, editing, ownerEntity, variableId, kind, value);
                    }
                }
                return changed;
            }
            case PropertyKind.Fixed64Vec3:
            {
                var fixedValue = value.Fixed64Vec3;
                Vector3 v = new Vector3(fixedValue.X.ToFloat(), fixedValue.Y.ToFloat(), fixedValue.Z.ToFloat());
                bool changed = ImVectorInput.DrawAt(widgetKey, x, y, width, ref v, float.MinValue, float.MaxValue, "F3");
                if (changed)
                {
                    value = PropertyValue.FromFixed64Vec3(FixedMath.Fixed64Vec3.FromFloat(v.X, v.Y, v.Z));
                    if (!isInstance)
                    {
                        int widgetId = Im.Context.GetId(widgetKey);
                        bool editing = Im.Context.AnyActive;
                        workspace.Commands.SetPrefabVariableDefault(widgetId, editing, ownerEntity, variableId, kind, value);
                    }
                }
                return changed;
            }
            case PropertyKind.PrefabRef:
            {
                uint current = value.UInt;
                bool missing = false;
                if (DrawPrefabDropdown(widgetKey, workspace, excludeStableId: 0, x, y, width, current, out uint next, out missing))
                {
                    value = PropertyValue.FromUInt(next);
                    if (!isInstance)
                    {
                        int widgetId = Im.Context.GetId(widgetKey);
                        workspace.Commands.SetPrefabVariableDefault(widgetId, isEditing: false, ownerEntity, variableId, kind, value);
                    }
                    return true;
                }

                if (missing && current != 0)
                {
                    Im.Text("(missing)".AsSpan(), x + width + 6f, y + (Im.Style.MinButtonHeight - Im.Style.FontSize) * 0.5f, Im.Style.FontSize, Im.Style.Secondary);
                }

                return false;
            }
            case PropertyKind.ShapeRef:
            {
                uint current = value.UInt;
                EntityId shapeScopeRoot = ownerEntity;
                if (isInstance &&
                    workspace.World.TryGetComponent(ownerEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) &&
                    instanceAny.IsValid)
                {
                    var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
                    var instance = PrefabInstanceComponent.Api.FromHandle(workspace.PropertyWorld, instanceHandle);
                    if (instance.IsAlive && instance.SourcePrefabStableId != 0)
                    {
                        EntityId source = workspace.World.GetEntityByStableId(instance.SourcePrefabStableId);
                        if (!source.IsNull && workspace.World.GetNodeType(source) == UiNodeType.Prefab)
                        {
                            shapeScopeRoot = source;
                        }
                    }
                }

                bool missing = false;
                if (DrawShapeDropdown(widgetKey, workspace, shapeScopeRoot, x, y, width, current, out uint next, out missing))
                {
                    value = PropertyValue.FromUInt(next);
                    if (!isInstance)
                    {
                        int widgetId = Im.Context.GetId(widgetKey);
                        workspace.Commands.SetPrefabVariableDefault(widgetId, isEditing: false, ownerEntity, variableId, kind, value);
                    }
                    return true;
                }

                if (missing && current != 0)
                {
                    Im.Text("(missing)".AsSpan(), x + width + 6f, y + (Im.Style.MinButtonHeight - Im.Style.FontSize) * 0.5f, Im.Style.FontSize, Im.Style.Secondary);
                }

                return false;
            }
            default:
                Im.Text("(unsupported)".AsSpan(), x, y + (Im.Style.MinButtonHeight - Im.Style.FontSize) * 0.5f, Im.Style.FontSize, Im.Style.TextSecondary);
                return false;
        }
    }

    private static bool DrawPrefabDropdown(
        string widgetKey,
        UiWorkspace workspace,
        uint excludeStableId,
        float x,
        float y,
        float width,
        uint currentStableId,
        out uint selectedStableId,
        out bool missing)
    {
        missing = false;
        selectedStableId = currentStableId;

        int count = 0;
        EnsureEntityDropdownCapacity(1);
        _entityDropdownOptions[count] = "(None)";
        _entityDropdownStableIds[count] = 0;
        count++;

        if (workspace.TryGetPrefabDropdownOptions(out string[] prefabOptions, out uint[] prefabStableIds))
        {
            int prefabCount = Math.Min(prefabStableIds.Length, prefabOptions.Length);
            for (int i = 0; i < prefabCount; i++)
            {
                uint stableId = prefabStableIds[i];
                if (stableId == 0 || stableId == excludeStableId)
                {
                    continue;
                }

                EnsureEntityDropdownCapacity(count + 1);
                _entityDropdownOptions[count] = prefabOptions[i];
                _entityDropdownStableIds[count] = stableId;
                count++;
            }
        }

        int selectedIndex = -1;
        for (int i = 0; i < count; i++)
        {
            if (_entityDropdownStableIds[i] == currentStableId)
            {
                selectedIndex = i;
                break;
            }
        }
        if (selectedIndex < 0 && currentStableId != 0)
        {
            missing = true;
        }

        bool changed = Im.Dropdown(widgetKey, _entityDropdownOptions.AsSpan(0, count), ref selectedIndex, x, y, width);
        if (changed && selectedIndex >= 0 && selectedIndex < count)
        {
            selectedStableId = _entityDropdownStableIds[selectedIndex];
        }

        return changed;
    }

    private static bool DrawShapeDropdown(
        string widgetKey,
        UiWorkspace workspace,
        EntityId scopeRoot,
        float x,
        float y,
        float width,
        uint currentStableId,
        out uint selectedStableId,
        out bool missing)
    {
        missing = false;
        selectedStableId = currentStableId;

        int count = 0;
        EnsureEntityDropdownCapacity(1);
        _entityDropdownOptions[count] = "(None)";
        _entityDropdownStableIds[count] = 0;
        count++;

        int stackCount = 0;
        if (!scopeRoot.IsNull)
        {
            EnsureTraversalCapacity(1);
            _entityTraversalScratch[stackCount++] = scopeRoot;
        }

        while (stackCount > 0)
        {
            EntityId current = _entityTraversalScratch[--stackCount];

            ReadOnlySpan<EntityId> children = workspace.World.GetChildren(current);
            for (int i = 0; i < children.Length; i++)
            {
                EntityId child = children[i];
                UiNodeType nodeType = workspace.World.GetNodeType(child);
                if (nodeType == UiNodeType.Shape)
                {
                    uint stableId = workspace.World.GetStableId(child);
                    if (stableId == 0)
                    {
                        continue;
                    }

                    EnsureEntityDropdownCapacity(count + 1);
                    if (workspace.TryGetLayerName(stableId, out string name) && !string.IsNullOrEmpty(name))
                    {
                        _entityDropdownOptions[count] = name;
                    }
                    else
                    {
                        _entityDropdownOptions[count] = $"Shape {stableId}";
                    }
                    _entityDropdownStableIds[count] = stableId;
                    count++;
                }

                // Traverse all descendants.
                if (nodeType != UiNodeType.None)
                {
                    EnsureTraversalCapacity(stackCount + 1);
                    _entityTraversalScratch[stackCount++] = child;
                }
            }
        }

        int selectedIndex = -1;
        for (int i = 0; i < count; i++)
        {
            if (_entityDropdownStableIds[i] == currentStableId)
            {
                selectedIndex = i;
                break;
            }
        }
        if (selectedIndex < 0 && currentStableId != 0)
        {
            missing = true;
        }

        bool changed = Im.Dropdown(widgetKey, _entityDropdownOptions.AsSpan(0, count), ref selectedIndex, x, y, width);
        if (changed && selectedIndex >= 0 && selectedIndex < count)
        {
            selectedStableId = _entityDropdownStableIds[selectedIndex];
        }

        return changed;
    }

    private static void EnsureEntityDropdownCapacity(int required)
    {
        if (_entityDropdownOptions.Length >= required && _entityDropdownStableIds.Length >= required)
        {
            return;
        }

        int next = Math.Max(required, _entityDropdownOptions.Length * 2);
        Array.Resize(ref _entityDropdownOptions, next);
        Array.Resize(ref _entityDropdownStableIds, next);
    }

    private static void EnsureTraversalCapacity(int required)
    {
        if (_entityTraversalScratch.Length >= required)
        {
            return;
        }

        int next = Math.Max(required, _entityTraversalScratch.Length * 2);
        Array.Resize(ref _entityTraversalScratch, next);
    }

    private static void DrawListVariableEditorForPrefab(UiWorkspace workspace, EntityId prefabEntity, ushort variableId, ref PropertyValue defaultValue, ImRect valueRect)
    {
        uint ownerStableId = workspace.World.GetStableId(prefabEntity);
        uint currentTypeStableId = defaultValue.UInt;

        bool missingType = false;
        if (DrawPrefabDropdown("list_type", workspace, excludeStableId: ownerStableId, valueRect.X, valueRect.Y, valueRect.Width, currentTypeStableId, out uint nextTypeStableId, out missingType))
        {
            workspace.Commands.SetPrefabListType(Im.Context.GetId("list_type"), isEditing: false, prefabEntity, variableId, nextTypeStableId);
            currentTypeStableId = nextTypeStableId;
        }

        if (currentTypeStableId != 0 && currentTypeStableId == ownerStableId)
        {
            DrawListInvalidTypeHint(valueRect, "Type cannot be self".AsSpan());
            return;
        }

        if (currentTypeStableId == 0)
        {
            DrawListMissingTypeHint(valueRect);
            return;
        }

        EntityId typePrefabEntity = workspace.World.GetEntityByStableId(currentTypeStableId);
        if (typePrefabEntity.IsNull || workspace.World.GetNodeType(typePrefabEntity) != UiNodeType.Prefab)
        {
            DrawListMissingTypeHint(valueRect);
            return;
        }

        DrawListItemsEditor(
            workspace,
            ownerEntity: prefabEntity,
            variableId: variableId,
            typePrefabEntity: typePrefabEntity,
            isReadOnly: false);
    }

    private static void DrawListVariableEditorForInstance(
        UiWorkspace workspace,
        EntityId instanceEntity,
        ushort variableId,
        ref PropertyValue value,
        bool overridden,
        ImRect valueRect,
        float rightOverlayWidth)
    {
        uint excludeStableId = 0;
        if (workspace.World.TryGetComponent(instanceEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) &&
            instanceAny.IsValid)
        {
            var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var instance = PrefabInstanceComponent.Api.FromHandle(workspace.PropertyWorld, instanceHandle);
            if (instance.IsAlive)
            {
                excludeStableId = instance.SourcePrefabStableId;
            }
        }

        uint currentTypeStableId = value.UInt;
        float editorWidth = Math.Max(1f, valueRect.Width - rightOverlayWidth);

        bool isReadOnly = workspace.World.HasComponent(instanceEntity, ListGeneratedComponent.Api.PoolIdConst);

        bool missingType = false;
        if (!isReadOnly &&
            DrawPrefabDropdown("list_type", workspace, excludeStableId, valueRect.X, valueRect.Y, editorWidth, currentTypeStableId, out uint nextTypeStableId, out missingType))
        {
            workspace.Commands.SetPrefabListType(Im.Context.GetId("list_type"), isEditing: false, instanceEntity, variableId, nextTypeStableId);
            currentTypeStableId = nextTypeStableId;
        }

        if (excludeStableId != 0 && currentTypeStableId != 0 && currentTypeStableId == excludeStableId)
        {
            DrawListInvalidTypeHint(valueRect, "Type cannot be self".AsSpan());
            return;
        }

        if (currentTypeStableId == 0)
        {
            DrawListMissingTypeHint(valueRect);
            return;
        }

        EntityId typePrefabEntity = workspace.World.GetEntityByStableId(currentTypeStableId);
        if (typePrefabEntity.IsNull || workspace.World.GetNodeType(typePrefabEntity) != UiNodeType.Prefab)
        {
            DrawListMissingTypeHint(valueRect);
            return;
        }

        _ = overridden;

        DrawListItemsEditor(
            workspace,
            ownerEntity: instanceEntity,
            variableId: variableId,
            typePrefabEntity: typePrefabEntity,
            isReadOnly: isReadOnly);
    }

    private static void DrawListMissingTypeHint(ImRect valueRect)
    {
        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = InspectorRow.GetPaddedRect(rect);
        Im.Text("Type required".AsSpan(), rect.X + 10f, rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f, Im.Style.FontSize, Im.Style.Secondary);
        _ = valueRect;
    }

    private static void DrawListInvalidTypeHint(ImRect valueRect, ReadOnlySpan<char> message)
    {
        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = InspectorRow.GetPaddedRect(rect);
        Im.Text(message, rect.X + 10f, rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f, Im.Style.FontSize, Im.Style.Secondary);
        _ = valueRect;
    }

    private static void DrawListItemsEditor(UiWorkspace workspace, EntityId ownerEntity, ushort variableId, EntityId typePrefabEntity, bool isReadOnly)
    {
        uint typeStableId = workspace.World.GetStableId(typePrefabEntity);
        if (typeStableId == 0)
        {
            return;
        }

        Span<char> itemLabelBuffer = stackalloc char[16];
        Span<char> fieldLabelBuffer = stackalloc char[24];

        Span<PropertyKind> fieldKinds = stackalloc PropertyKind[PrefabListDataComponent.MaxFieldsPerItem];
        Span<StringHandle> fieldNames = stackalloc StringHandle[PrefabListDataComponent.MaxFieldsPerItem];
        Span<PropertyValue> fieldDefaults = stackalloc PropertyValue[PrefabListDataComponent.MaxFieldsPerItem];
        int schemaCount = BuildListFieldSchema(workspace, typePrefabEntity, fieldKinds, fieldNames, fieldDefaults);

        bool hasListData = TryResolveListDataForDisplay(workspace, ownerEntity, variableId, out PrefabListDataComponent.ViewProxy list, out int entryIndex);

        int itemCount = 0;
        int itemStart = 0;
        int stride = 0;
        ReadOnlySpan<PropertyValue> items = default;

        if (hasListData)
        {
            ReadOnlySpan<ushort> entryItemCount = list.EntryItemCountReadOnlySpan();
            ReadOnlySpan<ushort> entryItemStart = list.EntryItemStartReadOnlySpan();
            ReadOnlySpan<ushort> entryFieldCount = list.EntryFieldCountReadOnlySpan();

            itemCount = entryItemCount[entryIndex];
            itemStart = entryItemStart[entryIndex];
            stride = entryFieldCount[entryIndex];
            items = list.ItemsReadOnlySpan();
        }

        if (!isReadOnly && hasListData && schemaCount != stride)
        {
            var hintRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
            hintRect = InspectorRow.GetPaddedRect(hintRect);
            float hintTextY = hintRect.Y + (hintRect.Height - Im.Style.FontSize) * 0.5f;
            Im.Text("List schema changed".AsSpan(), hintRect.X, hintTextY, Im.Style.FontSize, Im.Style.TextSecondary);

            float buttonWidth = 140f;
            var buttonRect = new ImRect(hintRect.Right - buttonWidth, hintRect.Y, buttonWidth, hintRect.Height);
            if (Im.Button("Update schema", buttonRect.X, buttonRect.Y, buttonRect.Width, buttonRect.Height))
            {
                workspace.Commands.UpdatePrefabListSchema(Im.Context.GetId("list_schema"), ownerEntity, variableId);
            }

            ImLayout.Space(4f);
        }

        int fieldCount = schemaCount;
        int storedStride = stride;

        for (int itemIndex = 0; itemIndex < itemCount; itemIndex++)
        {
            var headerRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
            headerRect = InspectorRow.GetPaddedRect(headerRect);
            const float listIndent = 12f;
            headerRect = new ImRect(headerRect.X + listIndent, headerRect.Y, Math.Max(1f, headerRect.Width - listIndent), headerRect.Height);

            float headerTextY = headerRect.Y + (headerRect.Height - Im.Style.FontSize) * 0.5f;

            Span<char> itemLabel = itemLabelBuffer;
            int itemLabelLen = 0;
            "Item ".AsSpan().CopyTo(itemLabel);
            itemLabelLen = 5;
            itemLabelLen += WriteInt(itemLabel.Slice(itemLabelLen), itemIndex);

            Im.Text(itemLabel.Slice(0, itemLabelLen), headerRect.X, headerTextY, Im.Style.FontSize, Im.Style.TextSecondary);

            if (!isReadOnly)
            {
                var removeRect = new ImRect(headerRect.Right - 28f, headerRect.Y, 28f, headerRect.Height);
                Im.Context.PushId(itemIndex);
                if (Im.Button("-", removeRect.X, removeRect.Y, removeRect.Width, removeRect.Height))
                {
                    workspace.Commands.RemovePrefabListItem(Im.Context.GetId("list_remove"), ownerEntity, variableId, itemIndex);
                }
                Im.Context.PopId();
            }

            for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
            {
                var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
                rowRect = InspectorRow.GetPaddedRect(rowRect);

                const float indent = 18f;
                rowRect = new ImRect(rowRect.X + indent, rowRect.Y, Math.Max(1f, rowRect.Width - indent), rowRect.Height);

                float labelWidth = Math.Clamp(rowRect.Width * 0.45f, 90f, 180f);
                var nameRect = new ImRect(rowRect.X, rowRect.Y, labelWidth, rowRect.Height);
                var valueRect = new ImRect(nameRect.Right + Im.Style.Spacing, rowRect.Y, Math.Max(40f, rowRect.Right - nameRect.Right - Im.Style.Spacing), rowRect.Height);

                float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
                StringHandle fieldName = fieldNames[fieldIndex];
                if (fieldName.IsValid)
                {
                    Im.Text(fieldName.ToString().AsSpan(), nameRect.X, textY, Im.Style.FontSize, Im.Style.TextSecondary);
                }
                else
                {
                    Span<char> fallback = fieldLabelBuffer;
                    "Field ".AsSpan().CopyTo(fallback);
                    int len = 6;
                    len += WriteInt(fallback.Slice(len), fieldIndex);
                    Im.Text(fallback.Slice(0, len), nameRect.X, textY, Im.Style.FontSize, Im.Style.TextSecondary);
                }

                PropertyKind fieldKind = fieldKinds[fieldIndex];
                PropertyValue fieldValue = default;
                if (hasListData && storedStride > 0 && fieldIndex < storedStride)
                {
                    int idx = itemStart + itemIndex * storedStride + fieldIndex;
                    if ((uint)idx < (uint)items.Length)
                    {
                        fieldValue = items[idx];
                    }
                    else
                    {
                        fieldValue = fieldDefaults[fieldIndex];
                    }
                }
                else
                {
                    fieldValue = fieldDefaults[fieldIndex];
                }

                if (!isReadOnly)
                {
                    Im.Context.PushId(itemIndex);
                    Im.Context.PushId(fieldIndex);
                    DrawListFieldEditor(workspace, ownerEntity, variableId, typePrefabEntity, itemIndex, fieldIndex, fieldKind, valueRect.X, valueRect.Y, valueRect.Width, ref fieldValue);
                    Im.Context.PopId();
                    Im.Context.PopId();
                }
                else
                {
                    DrawListFieldEditorReadOnly(fieldKind, fieldValue, valueRect);
                }
            }

            ImLayout.Space(4f);
        }

        if (!isReadOnly)
        {
            var addRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
            addRect = InspectorRow.GetPaddedRect(addRect);
            const float listIndent = 12f;
            addRect = new ImRect(addRect.X + listIndent, addRect.Y, Math.Max(1f, addRect.Width - listIndent), addRect.Height);
            if (Im.Button("+", addRect.X, addRect.Y, addRect.Width, addRect.Height))
            {
                workspace.Commands.AddPrefabListItem(Im.Context.GetId("list_add"), ownerEntity, variableId);
            }
        }
    }

    private static void DrawListFieldEditor(UiWorkspace workspace, EntityId ownerEntity, ushort variableId, EntityId typePrefabEntity, int itemIndex, int fieldIndex, PropertyKind kind, float x, float y, float width, ref PropertyValue value)
    {
        switch (kind)
        {
            case PropertyKind.Float:
            {
                float v = value.Float;
                if (ImScalarInput.DrawAt("list_field", x, y, width, ref v, float.MinValue, float.MaxValue, "F2"))
                {
                    value = PropertyValue.FromFloat(v);
                    int widgetId = Im.Context.GetId("list_field");
                    bool editing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);
                    workspace.Commands.SetPrefabListItemField(widgetId, editing, ownerEntity, variableId, itemIndex, fieldIndex, kind, value);
                }
                return;
            }
            case PropertyKind.Int:
            {
                float floatValue = value.Int;
                if (ImScalarInput.DrawAt("list_field", x, y, width, ref floatValue, int.MinValue, int.MaxValue, "F0"))
                {
                    int intValue = (int)MathF.Round(floatValue);
                    value = PropertyValue.FromInt(intValue);
                    int widgetId = Im.Context.GetId("list_field");
                    bool editing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);
                    workspace.Commands.SetPrefabListItemField(widgetId, editing, ownerEntity, variableId, itemIndex, fieldIndex, kind, value);
                }
                return;
            }
            case PropertyKind.Bool:
            {
                bool v = value.Bool;
                var checkboxRect = new ImRect(x, y, width, Im.Style.MinButtonHeight);
                Im.DrawRoundedRect(checkboxRect.X, checkboxRect.Y, checkboxRect.Width, checkboxRect.Height, Im.Style.CornerRadius, Im.Style.Surface);
                Im.DrawRoundedRectStroke(checkboxRect.X, checkboxRect.Y, checkboxRect.Width, checkboxRect.Height, Im.Style.CornerRadius, Im.Style.Border, Im.Style.BorderWidth);
                float checkboxX = checkboxRect.X + Im.Style.Padding;
                float checkboxY = checkboxRect.Y + (checkboxRect.Height - Im.Style.CheckboxSize) * 0.5f;
                if (Im.Checkbox("list_field", ref v, checkboxX, checkboxY))
                {
                    value = PropertyValue.FromBool(v);
                    workspace.Commands.SetPrefabListItemField(Im.Context.GetId("list_field"), isEditing: false, ownerEntity, variableId, itemIndex, fieldIndex, kind, value);
                }
                return;
            }
            case PropertyKind.Vec2:
            {
                Vector2 v = value.Vec2;
                if (ImVectorInput.DrawAt("list_field", x, y, width, ref v, float.MinValue, float.MaxValue, "F2"))
                {
                    value = PropertyValue.FromVec2(v);
                    int widgetId = Im.Context.GetId("list_field");
                    bool editing = Im.Context.AnyActive;
                    workspace.Commands.SetPrefabListItemField(widgetId, editing, ownerEntity, variableId, itemIndex, fieldIndex, kind, value);
                }
                return;
            }
            case PropertyKind.Vec3:
            {
                Vector3 v = value.Vec3;
                if (ImVectorInput.DrawAt("list_field", x, y, width, ref v, float.MinValue, float.MaxValue, "F2"))
                {
                    value = PropertyValue.FromVec3(v);
                    int widgetId = Im.Context.GetId("list_field");
                    bool editing = Im.Context.AnyActive;
                    workspace.Commands.SetPrefabListItemField(widgetId, editing, ownerEntity, variableId, itemIndex, fieldIndex, kind, value);
                }
                return;
            }
            case PropertyKind.Vec4:
            {
                Vector4 v = value.Vec4;
                if (ImVectorInput.DrawAt("list_field", x, y, width, ref v, float.MinValue, float.MaxValue, "F2"))
                {
                    value = PropertyValue.FromVec4(v);
                    int widgetId = Im.Context.GetId("list_field");
                    bool editing = Im.Context.AnyActive;
                    workspace.Commands.SetPrefabListItemField(widgetId, editing, ownerEntity, variableId, itemIndex, fieldIndex, kind, value);
                }
                return;
            }
            case PropertyKind.Color32:
            {
                Im.Text("(color)".AsSpan(), x, y + (Im.Style.MinButtonHeight - Im.Style.FontSize) * 0.5f, Im.Style.FontSize, Im.Style.TextSecondary);
                return;
            }
            case PropertyKind.StringHandle:
            {
                uint stableId = workspace.World.GetStableId(ownerEntity);
                ulong stateKey = MakeListTextStateKey(stableId, variableId, itemIndex, fieldIndex);
                TextEditState state = GetOrCreateTextState(stateKey, StringBufferSize);

                StringHandle current = value.StringHandle;
                int widgetId = Im.Context.GetId("list_field");
                bool wasFocused = Im.Context.IsFocused(widgetId);
                if (!wasFocused && state.Handle != current)
                {
                    SetTextBufferFromHandle(state, current);
                }

                int length = state.Length;
                if (Im.TextInput("list_field", state.Buffer, ref length, state.Buffer.Length, x, y, width))
                {
                    StringHandle newHandle = new string(state.Buffer, 0, length);
                    value = PropertyValue.FromStringHandle(newHandle);
                    state.Handle = newHandle;
                    bool editing = Im.Context.IsFocused(widgetId);
                    workspace.Commands.SetPrefabListItemField(widgetId, editing, ownerEntity, variableId, itemIndex, fieldIndex, kind, value);
                }
                state.Length = length;
                return;
            }
            case PropertyKind.Fixed64:
            {
                float v = value.Fixed64.ToFloat();
                if (ImScalarInput.DrawAt("list_field", x, y, width, ref v, float.MinValue, float.MaxValue, "F3"))
                {
                    value = PropertyValue.FromFixed64(FixedMath.Fixed64.FromFloat(v));
                    int widgetId = Im.Context.GetId("list_field");
                    bool editing = Im.Context.IsActive(widgetId) || Im.Context.IsFocused(widgetId);
                    workspace.Commands.SetPrefabListItemField(widgetId, editing, ownerEntity, variableId, itemIndex, fieldIndex, kind, value);
                }
                return;
            }
            case PropertyKind.Fixed64Vec2:
            {
                var fixedValue = value.Fixed64Vec2;
                Vector2 v = new Vector2(fixedValue.X.ToFloat(), fixedValue.Y.ToFloat());
                if (ImVectorInput.DrawAt("list_field", x, y, width, ref v, float.MinValue, float.MaxValue, "F3"))
                {
                    value = PropertyValue.FromFixed64Vec2(FixedMath.Fixed64Vec2.FromFloat(v.X, v.Y));
                    int widgetId = Im.Context.GetId("list_field");
                    bool editing = Im.Context.AnyActive;
                    workspace.Commands.SetPrefabListItemField(widgetId, editing, ownerEntity, variableId, itemIndex, fieldIndex, kind, value);
                }
                return;
            }
            case PropertyKind.Fixed64Vec3:
            {
                var fixedValue = value.Fixed64Vec3;
                Vector3 v = new Vector3(fixedValue.X.ToFloat(), fixedValue.Y.ToFloat(), fixedValue.Z.ToFloat());
                if (ImVectorInput.DrawAt("list_field", x, y, width, ref v, float.MinValue, float.MaxValue, "F3"))
                {
                    value = PropertyValue.FromFixed64Vec3(FixedMath.Fixed64Vec3.FromFloat(v.X, v.Y, v.Z));
                    int widgetId = Im.Context.GetId("list_field");
                    bool editing = Im.Context.AnyActive;
                    workspace.Commands.SetPrefabListItemField(widgetId, editing, ownerEntity, variableId, itemIndex, fieldIndex, kind, value);
                }
                return;
            }
            case PropertyKind.PrefabRef:
            {
                uint current = value.UInt;
                if (DrawPrefabDropdown("list_field", workspace, excludeStableId: 0, x, y, width, current, out uint next, out _))
                {
                    value = PropertyValue.FromUInt(next);
                    workspace.Commands.SetPrefabListItemField(Im.Context.GetId("list_field"), isEditing: false, ownerEntity, variableId, itemIndex, fieldIndex, kind, value);
                }
                return;
            }
            case PropertyKind.ShapeRef:
            {
                uint current = value.UInt;
                bool missing = false;
                if (DrawShapeDropdown("list_field", workspace, typePrefabEntity, x, y, width, current, out uint next, out missing))
                {
                    value = PropertyValue.FromUInt(next);
                    workspace.Commands.SetPrefabListItemField(Im.Context.GetId("list_field"), isEditing: false, ownerEntity, variableId, itemIndex, fieldIndex, kind, value);
                }
                _ = missing;
                return;
            }
            default:
                Im.Text("(unsupported)".AsSpan(), x, y + (Im.Style.MinButtonHeight - Im.Style.FontSize) * 0.5f, Im.Style.FontSize, Im.Style.TextSecondary);
                return;
        }
    }

    private static void DrawListFieldEditorReadOnly(PropertyKind kind, in PropertyValue value, ImRect rect)
    {
        string text = kind switch
        {
            PropertyKind.Float => value.Float.ToString("F2"),
            PropertyKind.Int => value.Int.ToString(),
            PropertyKind.Bool => value.Bool ? "true" : "false",
            PropertyKind.Trigger => "trigger",
            _ => "(read-only)"
        };

        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(text.AsSpan(), rect.X, textY, Im.Style.FontSize, Im.Style.TextSecondary);
    }

    private static int BuildListFieldSchema(
        UiWorkspace workspace,
        EntityId typePrefabEntity,
        Span<PropertyKind> kinds,
        Span<StringHandle> names,
        Span<PropertyValue> defaults)
    {
        if (typePrefabEntity.IsNull || workspace.World.GetNodeType(typePrefabEntity) != UiNodeType.Prefab)
        {
            return 0;
        }

        if (!workspace.World.TryGetComponent(typePrefabEntity, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) || varsAny.IsNull)
        {
            return 0;
        }

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        var vars = PrefabVariablesComponent.Api.FromHandle(workspace.PropertyWorld, varsHandle);
        if (!vars.IsAlive || vars.VariableCount == 0)
        {
            return 0;
        }

        ushort varCount = vars.VariableCount;
        if (varCount > PrefabVariablesComponent.MaxVariables)
        {
            varCount = (ushort)PrefabVariablesComponent.MaxVariables;
        }

        ReadOnlySpan<ushort> ids = vars.VariableIdReadOnlySpan();
        ReadOnlySpan<StringHandle> varNames = vars.NameReadOnlySpan();
        ReadOnlySpan<int> varKinds = vars.KindReadOnlySpan();
        ReadOnlySpan<PropertyValue> varDefaults = vars.DefaultValueReadOnlySpan();

        int outCount = 0;
        for (int i = 0; i < varCount && outCount < PrefabListDataComponent.MaxFieldsPerItem; i++)
        {
            if (ids[i] == 0)
            {
                continue;
            }

            PropertyKind kind = (PropertyKind)varKinds[i];
            if (kind == PropertyKind.List)
            {
                continue;
            }

            kinds[outCount] = kind;
            names[outCount] = varNames[i];
            defaults[outCount] = varDefaults[i];
            outCount++;
        }

        return outCount;
    }

    private static bool TryResolveListDataForDisplay(UiWorkspace workspace, EntityId ownerEntity, ushort variableId, out PrefabListDataComponent.ViewProxy list, out int entryIndex)
    {
        list = default;
        entryIndex = -1;

        if (ownerEntity.IsNull || variableId == 0)
        {
            return false;
        }

        bool tryInstanceFirst = workspace.World.GetNodeType(ownerEntity) == UiNodeType.PrefabInstance;
        if (tryInstanceFirst &&
            workspace.World.TryGetComponent(ownerEntity, PrefabListDataComponent.Api.PoolIdConst, out AnyComponentHandle listAny) &&
            listAny.IsValid)
        {
            var listHandle = new PrefabListDataComponentHandle(listAny.Index, listAny.Generation);
            list = PrefabListDataComponent.Api.FromHandle(workspace.PropertyWorld, listHandle);
            if (list.IsAlive && TryFindListEntryIndex(list, variableId, out entryIndex))
            {
                return true;
            }
        }

        if (tryInstanceFirst &&
            workspace.World.TryGetComponent(ownerEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) &&
            instanceAny.IsValid)
        {
            var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var instance = PrefabInstanceComponent.Api.FromHandle(workspace.PropertyWorld, instanceHandle);
            if (instance.IsAlive && instance.SourcePrefabStableId != 0)
            {
                EntityId sourcePrefabEntity = workspace.World.GetEntityByStableId(instance.SourcePrefabStableId);
                if (!sourcePrefabEntity.IsNull &&
                    workspace.World.TryGetComponent(sourcePrefabEntity, PrefabListDataComponent.Api.PoolIdConst, out AnyComponentHandle srcAny) &&
                    srcAny.IsValid)
                {
                    var listHandle = new PrefabListDataComponentHandle(srcAny.Index, srcAny.Generation);
                    list = PrefabListDataComponent.Api.FromHandle(workspace.PropertyWorld, listHandle);
                    if (list.IsAlive && TryFindListEntryIndex(list, variableId, out entryIndex))
                    {
                        return true;
                    }
                }
            }
        }

        if (!tryInstanceFirst &&
            workspace.World.TryGetComponent(ownerEntity, PrefabListDataComponent.Api.PoolIdConst, out AnyComponentHandle prefabListAny) &&
            prefabListAny.IsValid)
        {
            var listHandle = new PrefabListDataComponentHandle(prefabListAny.Index, prefabListAny.Generation);
            list = PrefabListDataComponent.Api.FromHandle(workspace.PropertyWorld, listHandle);
            if (list.IsAlive && TryFindListEntryIndex(list, variableId, out entryIndex))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFindListEntryIndex(PrefabListDataComponent.ViewProxy list, ushort variableId, out int entryIndex)
    {
        entryIndex = -1;

        ushort entryCount = list.EntryCount;
        if (entryCount == 0)
        {
            return false;
        }

        int n = entryCount > PrefabListDataComponent.MaxListEntries ? PrefabListDataComponent.MaxListEntries : entryCount;
        ReadOnlySpan<ushort> ids = list.EntryVariableIdReadOnlySpan();
        for (int i = 0; i < n; i++)
        {
            if (ids[i] == variableId)
            {
                entryIndex = i;
                return true;
            }
        }

        return false;
    }

    private static int WriteInt(Span<char> dst, int value)
    {
        if (dst.IsEmpty)
        {
            return 0;
        }

        if (value == 0)
        {
            dst[0] = '0';
            return 1;
        }

        int v = value;
        if (v < 0)
        {
            dst[0] = '-';
            int written = WriteInt(dst.Slice(1), -v);
            return written + 1;
        }

        Span<char> scratch = stackalloc char[12];
        int len = 0;
        while (v > 0 && len < scratch.Length)
        {
            int digit = v % 10;
            scratch[len++] = (char)('0' + digit);
            v /= 10;
        }

        int outLen = Math.Min(len, dst.Length);
        for (int i = 0; i < outLen; i++)
        {
            dst[i] = scratch[len - 1 - i];
        }

        return outLen;
    }

    private static ulong MakeListTextStateKey(uint stableId, ushort variableId, int itemIndex, int fieldIndex)
    {
        ulong key = stableId;
        key = (key << 16) ^ variableId;
        key = (key << 16) ^ (ushort)itemIndex;
        key = (key << 16) ^ (ushort)fieldIndex;
        return key;
    }

    private static void WriteHexByte(Span<char> dst, byte value)
    {
        const string digits = "0123456789ABCDEF";
        dst[0] = digits[(value >> 4) & 0xF];
        dst[1] = digits[value & 0xF];
    }

    private static int FindInstanceValueIndex(ReadOnlySpan<ushort> ids, int count, ushort variableId)
    {
        int n = Math.Min(count, ids.Length);
        for (int i = 0; i < n; i++)
        {
            if (ids[i] == variableId)
            {
                return i;
            }
        }
        return -1;
    }

    private static TextEditState GetOrCreateTextState(ulong key, int minCapacity)
    {
        if (!TextStates.TryGetValue(key, out TextEditState? state) || state == null)
        {
            state = new TextEditState();
            TextStates[key] = state;
        }

        if (state.Buffer.Length < minCapacity)
        {
            state.Buffer = new char[minCapacity];
            state.Length = 0;
            state.Handle = default;
        }

        return state;
    }

    private static void SetTextBufferFromHandle(TextEditState state, StringHandle handle)
    {
        string text = handle.IsValid ? handle.ToString() : string.Empty;
        int len = Math.Min(text.Length, state.Buffer.Length);
        text.AsSpan(0, len).CopyTo(state.Buffer);
        state.Length = len;
        state.Handle = handle;
    }

    private static ulong MakeTextStateKey(uint entityStableId, ushort variableId, uint field)
    {
        return ((ulong)entityStableId << 32) | ((ulong)field << 16) | variableId;
    }
}
