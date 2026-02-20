using System;
using System.Numerics;
using Core;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;
using Pooled.Runtime;
using Property;
using Property.Runtime;

namespace Derp.UI;

internal static class RuntimeDebugPanel
{
    public static void Draw(UiWorkspace workspace)
    {
        InspectorCard.Begin("Runtime Debug");

        if (!workspace.IsRuntimeMode)
        {
            InspectorHint.Draw("Enter Play mode to inspect runtime state.");
            InspectorCard.End();
            return;
        }

        if (!workspace.TryGetActivePrefabEntity(out EntityId prefabEntity) || prefabEntity.IsNull)
        {
            InspectorHint.Draw("No active prefab.");
            InspectorCard.End();
            return;
        }

        DrawRuntimeInput(workspace, prefabEntity);
        ImLayout.Space(8f);
        DrawStateMachine(workspace, prefabEntity);
        ImLayout.Space(8f);
        DrawHoveredListenerBindings(workspace, prefabEntity);

        InspectorCard.End();
    }

    private static void DrawRuntimeInput(UiWorkspace workspace, EntityId prefabEntity)
    {
        ref readonly UiPointerFrameInput input = ref workspace.RuntimeInput.Current;
        UiWorkspace.RuntimeEventDebug events = workspace.GetRuntimeEventDebug();

        DrawBoolRow("Pointer Valid", input.PointerValid);
        DrawVec2Row("Pointer World", input.PointerWorld);
        DrawBoolRow("Primary Down", input.PrimaryDown);
        DrawFloatRow("Wheel Delta", input.WheelDelta);

        uint hoveredHitStableId = 0;
        if (input.PointerValid)
        {
            hoveredHitStableId = workspace.HitTestHoveredStableIdForRuntime(prefabEntity, input.PointerWorld);
        }

        DrawU32Row("Hit StableId", hoveredHitStableId);
        DrawU32Row("Listener StableId", events.HoveredListenerStableId);
        DrawU32Row("Pressed StableId", events.PressedListenerStableId);
    }

    private static void DrawStateMachine(UiWorkspace workspace, EntityId prefabEntity)
    {
        UiWorld world = workspace.World;
        if (!world.TryGetComponent(prefabEntity, StateMachineRuntimeComponent.Api.PoolIdConst, out AnyComponentHandle runtimeAny) || !runtimeAny.IsValid)
        {
            InspectorHint.Draw("No state machine runtime on active prefab.");
            return;
        }

        if (!world.TryGetComponent(prefabEntity, StateMachineDefinitionComponent.Api.PoolIdConst, out AnyComponentHandle defAny) || !defAny.IsValid)
        {
            InspectorHint.Draw("No state machine definition on active prefab.");
            return;
        }

        var runtimeHandle = new StateMachineRuntimeComponentHandle(runtimeAny.Index, runtimeAny.Generation);
        var runtime = StateMachineRuntimeComponent.Api.FromHandle(workspace.PropertyWorld, runtimeHandle);
        if (!runtime.IsAlive)
        {
            InspectorHint.Draw("State machine runtime component is not alive.");
            return;
        }

        var defHandle = new StateMachineDefinitionComponentHandle(defAny.Index, defAny.Generation);
        var def = StateMachineDefinitionComponent.Api.FromHandle(workspace.PropertyWorld, defHandle);
        if (!def.IsAlive)
        {
            InspectorHint.Draw("State machine definition component is not alive.");
            return;
        }

        ushort machineId = (ushort)Math.Clamp(runtime.ActiveMachineId, 0, ushort.MaxValue);
        DrawU32Row("Active Machine", machineId);
        DrawU32Row("Debug Layer", (uint)Math.Clamp(runtime.DebugActiveLayerId, 0, ushort.MaxValue));
        DrawU32Row("Debug State", (uint)Math.Clamp(runtime.DebugActiveStateId, 0, ushort.MaxValue));
        DrawU32Row("Last Transition", (uint)Math.Clamp(runtime.DebugLastTransitionId, 0, ushort.MaxValue));

        ushort layerCount = def.LayerCount;
        if (layerCount > StateMachineDefinitionComponent.MaxLayers)
        {
            layerCount = StateMachineDefinitionComponent.MaxLayers;
        }

        Span<ushort> currentStateId = runtime.LayerCurrentStateIdSpan();
        Span<ushort> transitionId = runtime.LayerTransitionIdSpan();
        Span<ushort> transitionFrom = runtime.LayerTransitionFromStateIdSpan();
        Span<ushort> transitionTo = runtime.LayerTransitionToStateIdSpan();
        Span<uint> transitionTime = runtime.LayerTransitionTimeUsSpan();
        Span<uint> transitionDuration = runtime.LayerTransitionDurationUsSpan();

        Span<char> buffer = stackalloc char[96];

        for (int layerSlot = 0; layerSlot < layerCount; layerSlot++)
        {
            if (machineId != 0 && def.LayerMachineId[layerSlot] != machineId)
            {
                continue;
            }

            ushort layerId = def.LayerId[layerSlot];
            if (layerId == 0)
            {
                continue;
            }

            ushort stateId = currentStateId[layerSlot];

            var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
            rowRect = InspectorRow.GetPaddedRect(rowRect);
            float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;

            int written = 0;

            written += WriteLiteral(buffer.Slice(written), "Layer ");
            written += WriteUShort(buffer.Slice(written), layerId);
            written += WriteLiteral(buffer.Slice(written), "  State ");
            written += WriteUShort(buffer.Slice(written), stateId);

            ushort tId = transitionId[layerSlot];
            if (tId != 0)
            {
                written += WriteLiteral(buffer.Slice(written), "  Transition ");
                written += WriteUShort(buffer.Slice(written), tId);
                written += WriteLiteral(buffer.Slice(written), " (");
                written += WriteUShort(buffer.Slice(written), transitionFrom[layerSlot]);
                written += WriteLiteral(buffer.Slice(written), "->");
                written += WriteUShort(buffer.Slice(written), transitionTo[layerSlot]);
                written += WriteLiteral(buffer.Slice(written), " ");
                written += WriteU32(buffer.Slice(written), transitionTime[layerSlot]);
                written += WriteLiteral(buffer.Slice(written), "/");
                written += WriteU32(buffer.Slice(written), transitionDuration[layerSlot]);
                written += WriteLiteral(buffer.Slice(written), "us)");
            }

            Im.Text(buffer.Slice(0, written), rowRect.X, textY, Im.Style.FontSize, Im.Style.TextSecondary);
        }
    }

    private static void DrawHoveredListenerBindings(UiWorkspace workspace, EntityId prefabEntity)
    {
        UiWorkspace.RuntimeEventDebug events = workspace.GetRuntimeEventDebug();
        if (events.HoveredListenerStableId == 0)
        {
            InspectorHint.Draw("No hovered listener (hover something with an Event Listener component).");
            return;
        }

        EntityId listenerEntity = workspace.World.GetEntityByStableId(events.HoveredListenerStableId);
        if (listenerEntity.IsNull)
        {
            InspectorHint.Draw("Hovered listener entity is missing.");
            return;
        }

        if (!workspace.World.TryGetComponent(listenerEntity, EventListenerComponent.Api.PoolIdConst, out AnyComponentHandle any) || !any.IsValid)
        {
            InspectorHint.Draw("Hovered entity has no Event Listener component.");
            return;
        }

        var handle = new EventListenerComponentHandle(any.Index, any.Generation);
        var listener = EventListenerComponent.Api.FromHandle(workspace.PropertyWorld, handle);
        if (!listener.IsAlive)
        {
            InspectorHint.Draw("Event Listener component is not alive.");
            return;
        }

        EntityId storeEntity = workspace.GetRuntimeVariableStoreForListener(prefabEntity, listenerEntity);
        if (storeEntity.IsNull)
        {
            InspectorHint.Draw("No variable store found for listener.");
            return;
        }

        if (!TryGetVariableSchemaForStore(workspace, storeEntity, out PrefabVariablesComponent.ViewProxy vars, out uint schemaStableId))
        {
            InspectorHint.Draw("No prefab variable schema found for variable store.");
            return;
        }

        DrawU32Row("Listener Store StableId", workspace.World.GetStableId(storeEntity));
        DrawU32Row("Schema Prefab StableId", schemaStableId);

        DrawListenerVar(workspace, storeEntity, vars, "Hover Var", listener.HoverVarId);
        DrawListenerVar(workspace, storeEntity, vars, "Hover Enter Trigger", listener.HoverEnterTriggerId);
        DrawListenerVar(workspace, storeEntity, vars, "Hover Exit Trigger", listener.HoverExitTriggerId);
        DrawListenerVar(workspace, storeEntity, vars, "Press Trigger", listener.PressTriggerId);
        DrawListenerVar(workspace, storeEntity, vars, "Release Trigger", listener.ReleaseTriggerId);
        DrawListenerVar(workspace, storeEntity, vars, "Click Trigger", listener.ClickTriggerId);
        DrawListenerVar(workspace, storeEntity, vars, "Child Hover Var", listener.ChildHoverVarId);
        DrawListenerVar(workspace, storeEntity, vars, "Child Hover Enter", listener.ChildHoverEnterTriggerId);
        DrawListenerVar(workspace, storeEntity, vars, "Child Hover Exit", listener.ChildHoverExitTriggerId);
    }

    private static void DrawListenerVar(UiWorkspace workspace, EntityId storeEntity, PrefabVariablesComponent.ViewProxy vars, string label, int variableId)
    {
        ushort id = ClampVariableId(variableId);
        if (id == 0)
        {
            DrawU32Row(label, 0);
            return;
        }

        if (!TryReadVariableValue(workspace, storeEntity, vars, id, out PropertyKind kind, out PropertyValue value, out bool overridden, out StringHandle name))
        {
            DrawU32Row(label, id);
            return;
        }

        var rowRect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rowRect = InspectorRow.GetPaddedRect(rowRect);
        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;

        float labelX = rowRect.X;
        float valueX = rowRect.X + rowRect.Width * 0.55f;

        Im.Text(label.AsSpan(), labelX, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        Span<char> buffer = stackalloc char[128];
        int written = 0;

        written += WriteUShort(buffer.Slice(written), id);
        if (name.IsValid)
        {
            written += WriteLiteral(buffer.Slice(written), " ");
            string s = name.ToString();
            int max = Math.Min(s.Length, buffer.Length - written);
            s.AsSpan(0, max).CopyTo(buffer.Slice(written));
            written += max;
        }

        written += WriteLiteral(buffer.Slice(written), " = ");
        written += WriteValue(buffer.Slice(written), kind, value);
        written += WriteLiteral(buffer.Slice(written), overridden ? " (override)" : " (default)");

        Im.Text(buffer.Slice(0, written), valueX, textY, Im.Style.FontSize, Im.Style.TextSecondary);
    }

    private static bool TryGetVariableSchemaForStore(UiWorkspace workspace, EntityId storeEntity, out PrefabVariablesComponent.ViewProxy vars, out uint schemaStableId)
    {
        vars = default;
        schemaStableId = 0;

        UiWorld world = workspace.World;
        World propertyWorld = workspace.PropertyWorld;

        UiNodeType type = world.GetNodeType(storeEntity);
        EntityId schemaPrefab = EntityId.Null;
        if (type == UiNodeType.Prefab)
        {
            schemaPrefab = storeEntity;
        }
        else if (type == UiNodeType.PrefabInstance)
        {
            if (!world.TryGetComponent(storeEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) || !instanceAny.IsValid)
            {
                return false;
            }

            var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var instance = PrefabInstanceComponent.Api.FromHandle(propertyWorld, instanceHandle);
            if (!instance.IsAlive || instance.SourcePrefabStableId == 0)
            {
                return false;
            }

            schemaPrefab = world.GetEntityByStableId(instance.SourcePrefabStableId);
        }

        if (schemaPrefab.IsNull || world.GetNodeType(schemaPrefab) != UiNodeType.Prefab)
        {
            return false;
        }

        schemaStableId = world.GetStableId(schemaPrefab);

        if (!world.TryGetComponent(schemaPrefab, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) || !varsAny.IsValid)
        {
            return false;
        }

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        vars = PrefabVariablesComponent.Api.FromHandle(propertyWorld, varsHandle);
        return vars.IsAlive;
    }

    private static bool TryReadVariableValue(
        UiWorkspace workspace,
        EntityId storeEntity,
        PrefabVariablesComponent.ViewProxy schemaVars,
        ushort variableId,
        out PropertyKind kind,
        out PropertyValue value,
        out bool overridden,
        out StringHandle name)
    {
        kind = default;
        value = default;
        overridden = false;
        name = default;

        if (!schemaVars.IsAlive || schemaVars.VariableCount == 0)
        {
            return false;
        }

        ushort count = schemaVars.VariableCount;
        if (count > PrefabVariablesComponent.MaxVariables)
        {
            count = PrefabVariablesComponent.MaxVariables;
        }

        ReadOnlySpan<ushort> ids = schemaVars.VariableIdReadOnlySpan();
        ReadOnlySpan<int> kinds = schemaVars.KindReadOnlySpan();
        ReadOnlySpan<PropertyValue> defaults = schemaVars.DefaultValueReadOnlySpan();
        ReadOnlySpan<StringHandle> names = schemaVars.NameReadOnlySpan();

        int schemaIndex = -1;
        for (int i = 0; i < count; i++)
        {
            if (ids[i] == variableId)
            {
                schemaIndex = i;
                break;
            }
        }

        if (schemaIndex < 0)
        {
            return false;
        }

        kind = (PropertyKind)kinds[schemaIndex];
        value = defaults[schemaIndex];
        name = names[schemaIndex];

        if (!workspace.World.TryGetComponent(storeEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) || !instanceAny.IsValid)
        {
            return true;
        }

        var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
        var instance = PrefabInstanceComponent.Api.FromHandle(workspace.PropertyWorld, instanceHandle);
        if (!instance.IsAlive || instance.ValueCount == 0)
        {
            return true;
        }

        ushort instanceCount = instance.ValueCount;
        if (instanceCount > PrefabInstanceComponent.MaxVariables)
        {
            instanceCount = PrefabInstanceComponent.MaxVariables;
        }

        ReadOnlySpan<ushort> instanceIds = instance.VariableIdReadOnlySpan();
        ReadOnlySpan<PropertyValue> instanceValues = instance.ValueReadOnlySpan();
        ulong mask = instance.OverrideMask;

        for (int i = 0; i < instanceCount; i++)
        {
            if (instanceIds[i] != variableId)
            {
                continue;
            }

            bool isOverridden = (mask & (1UL << i)) != 0;
            overridden = isOverridden;
            if (isOverridden)
            {
                value = instanceValues[i];
            }
            return true;
        }

        return true;
    }

    private static ushort ClampVariableId(int id)
    {
        if (id <= 0)
        {
            return 0;
        }

        if (id >= ushort.MaxValue)
        {
            return ushort.MaxValue;
        }

        return (ushort)id;
    }

    private static void DrawBoolRow(string label, bool value)
    {
        ReadOnlySpan<char> v = value ? "True".AsSpan() : "False".AsSpan();
        DrawTextRow(label, v);
    }

    private static void DrawFloatRow(string label, float value)
    {
        Span<char> buffer = stackalloc char[32];
        if (!value.TryFormat(buffer, out int written, "0.###"))
        {
            written = 0;
        }
        DrawTextRow(label, buffer.Slice(0, written));
    }

    private static void DrawVec2Row(string label, Vector2 value)
    {
        Span<char> buffer = stackalloc char[64];
        int written = 0;
        written += WriteLiteral(buffer.Slice(written), "(");
        if (!value.X.TryFormat(buffer.Slice(written), out int w0, "0.###"))
        {
            w0 = 0;
        }
        written += w0;
        written += WriteLiteral(buffer.Slice(written), ", ");
        if (!value.Y.TryFormat(buffer.Slice(written), out int w1, "0.###"))
        {
            w1 = 0;
        }
        written += w1;
        written += WriteLiteral(buffer.Slice(written), ")");
        DrawTextRow(label, buffer.Slice(0, written));
    }

    private static void DrawU32Row(string label, uint value)
    {
        Span<char> buffer = stackalloc char[32];
        int written = WriteU32(buffer, value);
        DrawTextRow(label, buffer.Slice(0, written));
    }

    private static void DrawTextRow(string label, ReadOnlySpan<char> value)
    {
        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = InspectorRow.GetPaddedRect(rect);
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;

        float labelX = rect.X;
        float valueX = rect.X + rect.Width * 0.55f;

        Im.Text(label.AsSpan(), labelX, textY, Im.Style.FontSize, Im.Style.TextPrimary);
        Im.Text(value, valueX, textY, Im.Style.FontSize, Im.Style.TextSecondary);
    }

    private static int WriteValue(Span<char> dst, PropertyKind kind, PropertyValue value)
    {
        switch (kind)
        {
            case PropertyKind.Bool:
            case PropertyKind.Trigger:
                return WriteLiteral(dst, value.Bool ? "True" : "False");
            case PropertyKind.Int:
                return WriteInt(dst, value.Int);
            case PropertyKind.Float:
                return WriteFloat(dst, value.Float);
            case PropertyKind.Vec2:
                return WriteVec2(dst, value.Vec2);
            case PropertyKind.Color32:
                return WriteColor32(dst, value.Color32);
            default:
                return WriteLiteral(dst, "—");
        }
    }

    private static int WriteVec2(Span<char> dst, Vector2 v)
    {
        int written = 0;
        written += WriteLiteral(dst.Slice(written), "(");
        written += WriteFloat(dst.Slice(written), v.X);
        written += WriteLiteral(dst.Slice(written), ", ");
        written += WriteFloat(dst.Slice(written), v.Y);
        written += WriteLiteral(dst.Slice(written), ")");
        return written;
    }

    private static int WriteFloat(Span<char> dst, float v)
    {
        if (!v.TryFormat(dst, out int written, "0.###"))
        {
            return 0;
        }
        return written;
    }

    private static int WriteInt(Span<char> dst, int v)
    {
        v.TryFormat(dst, out int written);
        return written;
    }

    private static int WriteU32(Span<char> dst, uint v)
    {
        v.TryFormat(dst, out int written);
        return written;
    }

    private static int WriteUShort(Span<char> dst, ushort v)
    {
        v.TryFormat(dst, out int written);
        return written;
    }

    private static int WriteLiteral(Span<char> dst, string text)
    {
        int n = Math.Min(dst.Length, text.Length);
        text.AsSpan(0, n).CopyTo(dst);
        return n;
    }

    private static int WriteColor32(Span<char> dst, Color32 c)
    {
        if (dst.Length < 9)
        {
            return 0;
        }

        dst[0] = '#';
        WriteHexByte(dst.Slice(1, 2), c.R);
        WriteHexByte(dst.Slice(3, 2), c.G);
        WriteHexByte(dst.Slice(5, 2), c.B);
        WriteHexByte(dst.Slice(7, 2), c.A);
        return 9;
    }

    private static void WriteHexByte(Span<char> dst, byte b)
    {
        const string Hex = "0123456789ABCDEF";
        dst[0] = Hex[b >> 4];
        dst[1] = Hex[b & 0xF];
    }
}
