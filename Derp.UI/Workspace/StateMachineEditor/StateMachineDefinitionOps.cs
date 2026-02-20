using System;
using Core;

namespace Derp.UI;

internal static class StateMachineDefinitionOps
{
    public static bool TryAddBlend1DChild(
        StateMachineDefinitionComponent.ViewProxy def,
        int stateSlot,
        float threshold,
        int timelineId)
    {
        if ((uint)stateSlot >= (uint)def.StateCount)
        {
            return false;
        }

        ushort total = def.BlendChildCount;
        if (total >= StateMachineDefinitionComponent.MaxBlendChildren)
        {
            return false;
        }

        Span<ushort> childStart = def.StateBlendChildStartSpan();
        Span<byte> childCount = def.StateBlendChildCountSpan();

        ushort start = childStart[stateSlot];
        byte count = childCount[stateSlot];
        if (count == byte.MaxValue)
        {
            return false;
        }

        if (count == 0)
        {
            // Treat first insertion as append to avoid disturbing other state ranges when start is still at default.
            start = total;
            childStart[stateSlot] = start;
        }

        uint insertIndexU = (uint)start + count;
        if (insertIndexU > total)
        {
            return false;
        }

        int insertIndex = (int)insertIndexU;

        Span<int> timelines = def.BlendChildTimelineIdSpan();
        Span<float> thresholds = def.BlendChildThresholdSpan();

        for (int i = total; i > insertIndex; i--)
        {
            timelines[i] = timelines[i - 1];
            thresholds[i] = thresholds[i - 1];
        }

        timelines[insertIndex] = timelineId;
        thresholds[insertIndex] = threshold;

        int stateCount = def.StateCount;
        for (int i = 0; i < stateCount; i++)
        {
            if (i == stateSlot)
            {
                continue;
            }

            ushort otherStart = childStart[i];
            if (childCount[i] == 0)
            {
                continue;
            }

            if (otherStart >= insertIndex)
            {
                childStart[i] = (ushort)(otherStart + 1);
            }
        }

        childCount[stateSlot] = (byte)(count + 1);
        def.BlendChildCount = (ushort)(total + 1);
        return true;
    }

    public static bool TryRemoveBlend1DChild(StateMachineDefinitionComponent.ViewProxy def, int stateSlot, int childIndex)
    {
        if ((uint)stateSlot >= (uint)def.StateCount)
        {
            return false;
        }

        ushort total = def.BlendChildCount;
        if (total == 0)
        {
            return false;
        }

        Span<ushort> childStart = def.StateBlendChildStartSpan();
        Span<byte> childCount = def.StateBlendChildCountSpan();

        ushort start = childStart[stateSlot];
        byte count = childCount[stateSlot];
        if ((uint)childIndex >= count)
        {
            return false;
        }

        int removeIndex = start + childIndex;
        if ((uint)removeIndex >= total)
        {
            return false;
        }

        Span<int> timelines = def.BlendChildTimelineIdSpan();
        Span<float> thresholds = def.BlendChildThresholdSpan();

        int lastIndex = total - 1;
        for (int i = removeIndex; i < lastIndex; i++)
        {
            timelines[i] = timelines[i + 1];
            thresholds[i] = thresholds[i + 1];
        }

        timelines[lastIndex] = 0;
        thresholds[lastIndex] = 0f;

        int stateCount = def.StateCount;
        for (int i = 0; i < stateCount; i++)
        {
            if (i == stateSlot)
            {
                continue;
            }

            byte otherCount = childCount[i];
            if (otherCount == 0)
            {
                continue;
            }

            ushort otherStart = childStart[i];
            if (otherStart > removeIndex)
            {
                childStart[i] = (ushort)(otherStart - 1);
            }
        }

        byte newCount = (byte)(count - 1);
        childCount[stateSlot] = newCount;
        def.BlendChildCount = (ushort)(total - 1);

        if (newCount == 0)
        {
            childStart[stateSlot] = def.BlendChildCount;
        }

        return true;
    }

    public static bool TryAddTransitionCondition(
        StateMachineDefinitionComponent.ViewProxy def,
        int transitionSlot,
        ushort variableId,
        StateMachineDefinitionComponent.ConditionOp op,
        PropertyValue compareValue)
    {
        if ((uint)transitionSlot >= (uint)def.TransitionCount)
        {
            return false;
        }

        ushort conditionCount = def.ConditionCount;
        if (conditionCount >= StateMachineDefinitionComponent.MaxConditions)
        {
            return false;
        }

        Span<ushort> transitionStart = def.TransitionConditionStartSpan();
        Span<byte> transitionCountPer = def.TransitionConditionCountSpan();
        ushort start = transitionStart[transitionSlot];
        byte count = transitionCountPer[transitionSlot];
        if (count == byte.MaxValue)
        {
            return false;
        }

        uint insertIndexU = (uint)start + count;
        if (insertIndexU > conditionCount)
        {
            return false;
        }

        int insertIndex = (int)insertIndexU;

        // Shift condition payloads to make room.
        Span<ushort> varIds = def.ConditionVariableIdSpan();
        Span<byte> ops = def.ConditionOpValueSpan();
        Span<PropertyValue> values = def.ConditionCompareValueSpan();
        for (int i = conditionCount; i > insertIndex; i--)
        {
            varIds[i] = varIds[i - 1];
            ops[i] = ops[i - 1];
            values[i] = values[i - 1];
        }

        varIds[insertIndex] = variableId;
        ops[insertIndex] = (byte)op;
        values[insertIndex] = compareValue;

        // Fix up transition starts after the insertion point.
        int totalTransitions = def.TransitionCount;
        for (int i = 0; i < totalTransitions; i++)
        {
            if (i == transitionSlot)
            {
                continue;
            }

            ushort otherStart = transitionStart[i];
            if (otherStart >= insertIndex)
            {
                transitionStart[i] = (ushort)(otherStart + 1);
            }
        }

        transitionCountPer[transitionSlot] = (byte)(count + 1);
        def.ConditionCount = (ushort)(conditionCount + 1);
        return true;
    }

    public static bool TryRemoveTransitionCondition(StateMachineDefinitionComponent.ViewProxy def, int transitionSlot, int conditionIndex)
    {
        if ((uint)transitionSlot >= (uint)def.TransitionCount)
        {
            return false;
        }

        ushort conditionCount = def.ConditionCount;
        if (conditionCount == 0)
        {
            return false;
        }

        Span<ushort> transitionStart = def.TransitionConditionStartSpan();
        Span<byte> transitionCountPer = def.TransitionConditionCountSpan();
        ushort start = transitionStart[transitionSlot];
        byte count = transitionCountPer[transitionSlot];
        if ((uint)conditionIndex >= count)
        {
            return false;
        }

        int removeIndex = start + conditionIndex;
        if ((uint)removeIndex >= conditionCount)
        {
            return false;
        }

        Span<ushort> varIds = def.ConditionVariableIdSpan();
        Span<byte> ops = def.ConditionOpValueSpan();
        Span<PropertyValue> values = def.ConditionCompareValueSpan();
        int lastIndex = conditionCount - 1;
        for (int i = removeIndex; i < lastIndex; i++)
        {
            varIds[i] = varIds[i + 1];
            ops[i] = ops[i + 1];
            values[i] = values[i + 1];
        }

        varIds[lastIndex] = 0;
        ops[lastIndex] = 0;
        values[lastIndex] = default;

        int totalTransitions = def.TransitionCount;
        for (int i = 0; i < totalTransitions; i++)
        {
            if (i == transitionSlot)
            {
                continue;
            }

            ushort otherStart = transitionStart[i];
            if (otherStart > removeIndex)
            {
                transitionStart[i] = (ushort)(otherStart - 1);
            }
        }

        transitionCountPer[transitionSlot] = (byte)(count - 1);
        def.ConditionCount = (ushort)(conditionCount - 1);
        return true;
    }

    public static void RemoveAllTransitionConditions(StateMachineDefinitionComponent.ViewProxy def, int transitionSlot)
    {
        if ((uint)transitionSlot >= (uint)def.TransitionCount)
        {
            return;
        }

        Span<ushort> transitionStart = def.TransitionConditionStartSpan();
        Span<byte> transitionCountPer = def.TransitionConditionCountSpan();
        ushort start = transitionStart[transitionSlot];
        byte count = transitionCountPer[transitionSlot];
        if (count == 0)
        {
            return;
        }

        RemoveConditionRange(def, transitionSlot, start, count);
    }

    private static void RemoveConditionRange(StateMachineDefinitionComponent.ViewProxy def, int removedTransitionSlot, ushort start, byte count)
    {
        ushort conditionCount = def.ConditionCount;
        if (count == 0 || start >= conditionCount)
        {
            def.TransitionConditionCountSpan()[removedTransitionSlot] = 0;
            return;
        }

        uint endU = (uint)start + count;
        if (endU > conditionCount)
        {
            // Clamp to avoid corrupting memory if data is invalid.
            count = (byte)(conditionCount - start);
            endU = conditionCount;
        }

        int end = (int)endU;
        int shiftCount = count;
        if (shiftCount <= 0)
        {
            def.TransitionConditionCountSpan()[removedTransitionSlot] = 0;
            return;
        }

        Span<ushort> varIds = def.ConditionVariableIdSpan();
        Span<byte> ops = def.ConditionOpValueSpan();
        Span<PropertyValue> values = def.ConditionCompareValueSpan();

        int lastIndex = conditionCount - 1;
        for (int i = start; i <= lastIndex - shiftCount; i++)
        {
            int src = i + shiftCount;
            varIds[i] = varIds[src];
            ops[i] = ops[src];
            values[i] = values[src];
        }

        for (int i = conditionCount - shiftCount; i < conditionCount; i++)
        {
            varIds[i] = 0;
            ops[i] = 0;
            values[i] = default;
        }

        Span<ushort> transitionStart = def.TransitionConditionStartSpan();
        Span<byte> transitionCountPer = def.TransitionConditionCountSpan();
        int totalTransitions = def.TransitionCount;
        for (int i = 0; i < totalTransitions; i++)
        {
            if (i == removedTransitionSlot)
            {
                continue;
            }

            ushort otherStart = transitionStart[i];
            if (otherStart > start)
            {
                transitionStart[i] = (ushort)(otherStart - shiftCount);
            }
        }

        transitionCountPer[removedTransitionSlot] = 0;
        transitionStart[removedTransitionSlot] = start;
        def.ConditionCount = (ushort)(conditionCount - shiftCount);
    }

    public static bool TryEnsureSelectedMachineId(StateMachineDefinitionComponent.ViewProxy def, out ushort machineId)
    {
        machineId = def.SelectedMachineId;
        if (machineId != 0)
        {
            return true;
        }

        int count = def.MachineCount;
        if (count <= 0)
        {
            return false;
        }

        machineId = def.MachineId[0];
        if (machineId == 0)
        {
            return false;
        }

        def.SelectedMachineId = machineId;
        return true;
    }

    public static bool TryFindMachineSlotIndexById(StateMachineDefinitionComponent.ViewProxy def, ushort machineId, out int machineSlotIndex)
    {
        machineSlotIndex = -1;
        if (machineId == 0)
        {
            return false;
        }

        int count = def.MachineCount;
        ReadOnlySpan<ushort> ids = def.MachineIdReadOnlySpan();
        for (int i = 0; i < count; i++)
        {
            if (ids[i] == machineId)
            {
                machineSlotIndex = i;
                return true;
            }
        }

        return false;
    }

    public static bool TryGetMachineName(StateMachineDefinitionComponent.ViewProxy def, ushort machineId, out StringHandle name)
    {
        name = default;
        if (!TryFindMachineSlotIndexById(def, machineId, out int slot))
        {
            return false;
        }

        name = def.MachineName[slot];
        return true;
    }

    public static int CollectLayerSlotIndicesForMachine(StateMachineDefinitionComponent.ViewProxy def, ushort machineId, Span<int> layerSlotIndices)
    {
        int written = 0;
        if (machineId == 0)
        {
            return 0;
        }

        int layerCount = def.LayerCount;
        ReadOnlySpan<ushort> layerMachineIds = def.LayerMachineIdReadOnlySpan();
        for (int i = 0; i < layerCount; i++)
        {
            if (layerMachineIds[i] != machineId)
            {
                continue;
            }

            if ((uint)written < (uint)layerSlotIndices.Length)
            {
                layerSlotIndices[written++] = i;
            }
        }

        return written;
    }

    public static bool TryFindLayerSlotIndexById(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, out int layerSlotIndex)
    {
        layerSlotIndex = -1;
        if (layerId == 0)
        {
            return false;
        }

        int count = def.LayerCount;
        ReadOnlySpan<ushort> ids = def.LayerIdReadOnlySpan();
        for (int i = 0; i < count; i++)
        {
            if (ids[i] == layerId)
            {
                layerSlotIndex = i;
                return true;
            }
        }

        return false;
    }

    public static bool TryGetLayerName(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, out StringHandle name)
    {
        name = default;
        if (!TryFindLayerSlotIndexById(def, layerId, out int slot))
        {
            return false;
        }

        name = def.LayerName[slot];
        return true;
    }

    public static bool TryRenameLayer(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, string committed)
    {
        if (!TryFindLayerSlotIndexById(def, layerId, out int slot))
        {
            return false;
        }

        Span<StringHandle> names = def.LayerNameSpan();
        names[slot] = committed;
        return true;
    }

    public static bool TryAddLayer(StateMachineDefinitionComponent.ViewProxy def, ushort machineId, string name)
    {
        int layerCount = def.LayerCount;
        if (layerCount >= StateMachineDefinitionComponent.MaxLayers || machineId == 0)
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
