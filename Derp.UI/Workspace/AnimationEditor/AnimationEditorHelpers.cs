using System;
using System.Collections.Generic;
using System.Numerics;
using Core;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Widgets;
using Pooled.Runtime;
using Property;
using Property.Runtime;

namespace Derp.UI;

internal static class AnimationEditorHelpers
{
    public static ulong MakeTargetKey(UiWorkspace workspace, EntityId entity)
    {
        uint stableId = workspace.World.GetStableId(entity);
        if (stableId == 0)
        {
            stableId = (uint)entity.Value;
        }
        return stableId;
    }

    public static ulong MakeComponentCollapseKey(UiWorkspace workspace, EntityId entity, ushort componentKind)
    {
        ulong targetKey = MakeTargetKey(workspace, entity);
        return (targetKey << 16) ^ componentKind;
    }

    public static float GetPixelsPerFrame(AnimationEditorState state)
    {
        state.Zoom = Math.Clamp(state.Zoom, 0.001f, 32f);
        return 8f * state.Zoom;
    }

    public static bool IsTargetCollapsed(UiWorkspace workspace, AnimationEditorState state, EntityId entity)
    {
        ulong key = MakeTargetKey(workspace, entity);
        for (int i = 0; i < state.CollapsedTargets.Count; i++)
        {
            if (state.CollapsedTargets[i] == key)
            {
                return true;
            }
        }
        return false;
    }

    public static void ToggleTargetCollapsed(UiWorkspace workspace, AnimationEditorState state, EntityId entity)
    {
        ulong key = MakeTargetKey(workspace, entity);
        for (int i = 0; i < state.CollapsedTargets.Count; i++)
        {
            if (state.CollapsedTargets[i] == key)
            {
                state.CollapsedTargets.RemoveAt(i);
                return;
            }
        }
        state.CollapsedTargets.Add(key);
    }

    public static bool IsComponentCollapsed(UiWorkspace workspace, AnimationEditorState state, EntityId entity, ushort componentKind)
    {
        ulong key = MakeComponentCollapseKey(workspace, entity, componentKind);
        for (int i = 0; i < state.CollapsedComponents.Count; i++)
        {
            if (state.CollapsedComponents[i] == key)
            {
                return true;
            }
        }
        return false;
    }

    public static void ToggleComponentCollapsed(UiWorkspace workspace, AnimationEditorState state, EntityId entity, ushort componentKind)
    {
        ulong key = MakeComponentCollapseKey(workspace, entity, componentKind);
        for (int i = 0; i < state.CollapsedComponents.Count; i++)
        {
            if (state.CollapsedComponents[i] == key)
            {
                state.CollapsedComponents.RemoveAt(i);
                return;
            }
        }
        state.CollapsedComponents.Add(key);
    }

    public static bool IsGroupCollapsed(AnimationEditorState state, ulong channelGroupKey)
    {
        for (int i = 0; i < state.CollapsedChannelGroups.Count; i++)
        {
            if (state.CollapsedChannelGroups[i] == channelGroupKey)
            {
                return true;
            }
        }
        return false;
    }

    public static void ToggleGroupCollapsed(AnimationEditorState state, ulong channelGroupKey)
    {
        for (int i = 0; i < state.CollapsedChannelGroups.Count; i++)
        {
            if (state.CollapsedChannelGroups[i] == channelGroupKey)
            {
                state.CollapsedChannelGroups.RemoveAt(i);
                return;
            }
        }
        state.CollapsedChannelGroups.Add(channelGroupKey);
    }

    public static AnimationDocument.AnimationTrack? FindTrack(AnimationDocument.AnimationTimeline timeline, AnimationDocument.AnimationBinding binding)
    {
        for (int i = 0; i < timeline.Tracks.Count; i++)
        {
            var track = timeline.Tracks[i];
            if (track.Binding == binding)
            {
                return track;
            }
        }
        return null;
    }

    public static bool HasKeyedTrack(AnimationDocument.AnimationTimeline timeline, in AnimationDocument.AnimationBinding binding)
    {
        var track = FindTrack(timeline, binding);
        return track != null && track.Keys.Count > 0;
    }

    public static bool HasAnyKeyedTrackForTarget(AnimationDocument.AnimationTimeline timeline, int targetIndex)
    {
        for (int i = 0; i < timeline.Tracks.Count; i++)
        {
            var track = timeline.Tracks[i];
            if (track.Binding.TargetIndex != targetIndex)
            {
                continue;
            }
            if (track.Keys.Count > 0)
            {
                return true;
            }
        }
        return false;
    }

    public static bool TryGetSelectionTargetStableId(UiWorkspace workspace, out uint stableId)
    {
        stableId = 0;

        if (workspace.TryGetAnimationTargetOverride(out EntityId overrideEntity) && TryGetStableId(workspace, overrideEntity, out stableId))
        {
            return true;
        }

        if (workspace._selectedEntities.Count > 0)
        {
            return TryGetStableId(workspace, workspace._selectedEntities[0], out stableId);
        }

        if (!workspace._selectedPrefabEntity.IsNull)
        {
            return TryGetStableId(workspace, workspace._selectedPrefabEntity, out stableId);
        }

        return false;
    }

    private static bool TryGetStableId(UiWorkspace workspace, EntityId entity, out uint stableId)
    {
        stableId = 0;
        if (entity.IsNull)
        {
            return false;
        }

        stableId = workspace.World.GetStableId(entity);
        return stableId != 0;
    }

    public static void AddTargetIfMissing(UiWorkspace workspace, AnimationDocument.AnimationTimeline timeline, EntityId entity)
    {
        if (TryGetStableId(workspace, entity, out uint stableId))
        {
            GetOrAddTargetIndex(timeline, stableId);
        }
    }

    public static bool TryGetTargetIndex(UiWorkspace workspace, AnimationDocument.AnimationTimeline timeline, EntityId entity, out int targetIndex)
    {
        if (!TryGetStableId(workspace, entity, out uint stableId))
        {
            targetIndex = -1;
            return false;
        }

        return TryGetTargetIndex(timeline, stableId, out targetIndex);
    }

    public static bool TryGetTargetIndex(AnimationDocument.AnimationTimeline timeline, uint stableId, out int targetIndex)
    {
        for (int i = 0; i < timeline.Targets.Count; i++)
        {
            if (timeline.Targets[i].StableId == stableId)
            {
                targetIndex = i;
                return true;
            }
        }

        targetIndex = -1;
        return false;
    }

    public static int GetOrAddTargetIndex(UiWorkspace workspace, AnimationDocument.AnimationTimeline timeline, EntityId entity)
    {
        if (!TryGetStableId(workspace, entity, out uint stableId))
        {
            return -1;
        }

        return GetOrAddTargetIndex(timeline, stableId);
    }

    public static int GetOrAddTargetIndex(AnimationDocument.AnimationTimeline timeline, uint stableId)
    {
        for (int i = 0; i < timeline.Targets.Count; i++)
        {
            if (timeline.Targets[i].StableId == stableId)
            {
                return i;
            }
        }

        timeline.Targets.Add(new AnimationDocument.AnimationTarget(stableId));
        return timeline.Targets.Count - 1;
    }

    public static bool HasKeyAtFrame(AnimationDocument.AnimationTimeline timeline, AnimationDocument.AnimationBinding binding, int frame)
    {
        var track = FindTrack(timeline, binding);
        if (track == null)
        {
            return false;
        }

        for (int i = 0; i < track.Keys.Count; i++)
        {
            if (track.Keys[i].Frame == frame)
            {
                return true;
            }
        }

        return false;
    }

    public static void AddOrUpdateKeyframeAtCurrentFrame(
        UiWorkspace workspace,
        AnimationEditorState state,
        AnimationDocument.AnimationTimeline timeline,
        AnimationDocument.AnimationBinding binding,
        bool selectKeyframe = true)
    {
        var track = FindTrack(timeline, binding);
        if (track == null)
        {
            track = new AnimationDocument.AnimationTrack
            {
                Binding = binding
            };
            timeline.Tracks.Add(track);
        }

        int durationFrames = Math.Max(1, timeline.DurationFrames);
        int frame = Math.Clamp(state.CurrentFrame, 0, durationFrames);

        PropertyValue value = default;
        if (TryResolveSlot(workspace, timeline, binding, out PropertySlot slot))
        {
            value = ReadValue(workspace.PropertyWorld, slot);
        }
        else if (binding.ComponentKind == PrefabInstanceComponent.Api.PoolIdConst &&
                 AnimationBindingIds.TryGetPrefabVariableId(binding.PropertyId, out ushort variableId) &&
                 binding.TargetIndex >= 0 &&
                 binding.TargetIndex < timeline.Targets.Count)
        {
            uint targetStableId = timeline.Targets[binding.TargetIndex].StableId;
            EntityId instanceEntity = workspace.World.GetEntityByStableId(targetStableId);
            if (!instanceEntity.IsNull && TryReadPrefabInstanceVariableValue(workspace, instanceEntity, variableId, out value))
            {
                // value already set
            }
        }

        for (int i = 0; i < track.Keys.Count; i++)
        {
            if (track.Keys[i].Frame == frame)
            {
                var k = track.Keys[i];
                k.Value = value;
                track.Keys[i] = k;
                if (selectKeyframe)
                {
                    state.Selected = new AnimationEditorState.SelectedKey { TimelineId = timeline.Id, Binding = track.Binding, KeyIndex = i };
                    state.SelectedTrack = new AnimationEditorState.TrackSelection { TimelineId = timeline.Id, Binding = track.Binding };
                    workspace.NotifyInspectorAnimationInteraction();
                }
                return;
            }
        }

        track.Keys.Add(new AnimationDocument.AnimationKeyframe
        {
            Frame = frame,
            Value = value,
            Interpolation = AnimationDocument.Interpolation.Cubic,
            InTangent = 0f,
            OutTangent = 0f
        });
        SortKeys(track.Keys);

        for (int i = 0; i < track.Keys.Count; i++)
        {
            if (track.Keys[i].Frame == frame)
            {
                if (selectKeyframe)
                {
                    state.Selected = new AnimationEditorState.SelectedKey { TimelineId = timeline.Id, Binding = track.Binding, KeyIndex = i };
                    state.SelectedTrack = new AnimationEditorState.TrackSelection { TimelineId = timeline.Id, Binding = track.Binding };
                    workspace.NotifyInspectorAnimationInteraction();
                }
                break;
            }
        }
    }

    public static bool TryResolveSlot(UiWorkspace workspace, AnimationDocument.AnimationTimeline timeline, AnimationDocument.AnimationBinding binding, out PropertySlot slot)
    {
        slot = default;

        if (binding.TargetIndex < 0 || binding.TargetIndex >= timeline.Targets.Count)
        {
            return false;
        }

        uint stableId = timeline.Targets[binding.TargetIndex].StableId;
        EntityId targetEntity = workspace.World.GetEntityByStableId(stableId);

        if (targetEntity.IsNull)
        {
            return false;
        }

        if (!workspace.World.TryGetComponent(targetEntity, binding.ComponentKind, out AnyComponentHandle component))
        {
            return false;
        }

        int propertyCount = PropertyDispatcher.GetPropertyCount(component);
        if (propertyCount <= 0)
        {
            return false;
        }

        int hintIndex = binding.PropertyIndexHint;
        if (hintIndex >= 0 && hintIndex < propertyCount)
        {
            if (PropertyDispatcher.TryGetInfo(component, hintIndex, out var hintInfo) && hintInfo.PropertyId == binding.PropertyId)
            {
                slot = new PropertySlot(component, (ushort)hintIndex, binding.PropertyId, binding.PropertyKind);
                return true;
            }
        }

        for (ushort i = 0; i < propertyCount; i++)
        {
            if (!PropertyDispatcher.TryGetInfo(component, i, out var info))
            {
                continue;
            }
            if (info.PropertyId == binding.PropertyId)
            {
                slot = new PropertySlot(component, i, binding.PropertyId, binding.PropertyKind);
                return true;
            }
        }

        return false;
    }

    public static ulong MakeChannelGroupKey(ushort componentKind, ulong channelGroupId)
    {
        return channelGroupId ^ ((ulong)componentKind << 48);
    }

    public static PropertyValue ReadValue(IPoolRegistry registry, in PropertySlot slot)
    {
        return slot.Kind switch
        {
            PropertyKind.Float => PropertyValue.FromFloat(PropertyDispatcher.ReadFloat(registry, slot)),
            PropertyKind.Int => PropertyValue.FromInt(PropertyDispatcher.ReadInt(registry, slot)),
            PropertyKind.Bool => PropertyValue.FromBool(PropertyDispatcher.ReadBool(registry, slot)),
            PropertyKind.Vec2 => PropertyValue.FromVec2(PropertyDispatcher.ReadVec2(registry, slot)),
            PropertyKind.Vec3 => PropertyValue.FromVec3(PropertyDispatcher.ReadVec3(registry, slot)),
            PropertyKind.Vec4 => PropertyValue.FromVec4(PropertyDispatcher.ReadVec4(registry, slot)),
            PropertyKind.Color32 => PropertyValue.FromColor32(PropertyDispatcher.ReadColor32(registry, slot)),
            PropertyKind.StringHandle => PropertyValue.FromStringHandle(PropertyDispatcher.ReadStringHandle(registry, slot)),
            PropertyKind.Fixed64 => PropertyValue.FromFixed64(PropertyDispatcher.ReadFixed64(registry, slot)),
            PropertyKind.Fixed64Vec2 => PropertyValue.FromFixed64Vec2(PropertyDispatcher.ReadFixed64Vec2(registry, slot)),
            PropertyKind.Fixed64Vec3 => PropertyValue.FromFixed64Vec3(PropertyDispatcher.ReadFixed64Vec3(registry, slot)),
            _ => default
        };
    }

    public static void WriteValue(IPoolRegistry registry, in PropertySlot slot, in PropertyValue value)
    {
        switch (slot.Kind)
        {
            case PropertyKind.Float:
                PropertyDispatcher.WriteFloat(registry, slot, value.Float);
                break;
            case PropertyKind.Int:
                PropertyDispatcher.WriteInt(registry, slot, value.Int);
                break;
            case PropertyKind.Bool:
                PropertyDispatcher.WriteBool(registry, slot, value.Bool);
                break;
            case PropertyKind.Vec2:
                PropertyDispatcher.WriteVec2(registry, slot, value.Vec2);
                break;
            case PropertyKind.Vec3:
                PropertyDispatcher.WriteVec3(registry, slot, value.Vec3);
                break;
            case PropertyKind.Vec4:
                PropertyDispatcher.WriteVec4(registry, slot, value.Vec4);
                break;
            case PropertyKind.Color32:
                PropertyDispatcher.WriteColor32(registry, slot, value.Color32);
                break;
            case PropertyKind.StringHandle:
                PropertyDispatcher.WriteStringHandle(registry, slot, value.StringHandle);
                break;
            case PropertyKind.Fixed64:
                PropertyDispatcher.WriteFixed64(registry, slot, value.Fixed64);
                break;
            case PropertyKind.Fixed64Vec2:
                PropertyDispatcher.WriteFixed64Vec2(registry, slot, value.Fixed64Vec2);
                break;
            case PropertyKind.Fixed64Vec3:
                PropertyDispatcher.WriteFixed64Vec3(registry, slot, value.Fixed64Vec3);
                break;
        }
    }

    public static void DrawDiamond(float cx, float cy, float size, uint color)
    {
        ImIcons.DrawDiamond(cx, cy, size, color);
    }

    public static void DrawFilledDiamond(float cx, float cy, float size, uint color)
    {
        ImIcons.DrawFilledDiamond(cx, cy, size, color);
    }

    public static void SortKeys(List<AnimationDocument.AnimationKeyframe> keys)
    {
        for (int i = 1; i < keys.Count; i++)
        {
            var key = keys[i];
            int j = i - 1;
            while (j >= 0 && keys[j].Frame > key.Frame)
            {
                keys[j + 1] = keys[j];
                j--;
            }
            keys[j + 1] = key;
        }
    }

    public static void ApplyTimelineAtFrame(UiWorkspace workspace, AnimationDocument.AnimationTimeline timeline, int frame)
    {
        if (timeline.Tracks.Count <= 0)
        {
            return;
        }

        int duration = Math.Max(1, timeline.DurationFrames);
        frame = Math.Clamp(frame, 0, duration);

        var registry = workspace.PropertyWorld;

        for (int trackIndex = 0; trackIndex < timeline.Tracks.Count; trackIndex++)
        {
            var track = timeline.Tracks[trackIndex];
            if (track.Keys.Count <= 0)
            {
                continue;
            }

            if (track.Binding.ComponentKind == PrefabInstanceComponent.Api.PoolIdConst &&
                AnimationBindingIds.TryGetPrefabVariableId(track.Binding.PropertyId, out ushort variableId) &&
                track.Binding.TargetIndex >= 0 &&
                track.Binding.TargetIndex < timeline.Targets.Count)
            {
                if (!TryEvaluateTrackAtFrame(track, frame, out PropertyValue value))
                {
                    continue;
                }

                uint stableId = timeline.Targets[track.Binding.TargetIndex].StableId;
                EntityId instanceEntity = workspace.World.GetEntityByStableId(stableId);
                if (!instanceEntity.IsNull)
                {
                    WritePrefabInstanceVariableOverrideNoUndo(workspace, instanceEntity, variableId, value);
                }
                continue;
            }

            if (!TryResolveSlot(workspace, timeline, track.Binding, out PropertySlot slot))
            {
                continue;
            }

            if (!TryEvaluateTrackAtFrame(track, frame, out PropertyValue evaluatedValue))
            {
                continue;
            }

            WriteValue(registry, slot, evaluatedValue);
        }
    }

    private static bool TryReadPrefabInstanceVariableValue(UiWorkspace workspace, EntityId instanceEntity, ushort variableId, out PropertyValue value)
    {
        value = default;

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
        if (sourcePrefabStableId == 0)
        {
            if (instanceIndex >= 0)
            {
                value = values[instanceIndex];
                return true;
            }
            return false;
        }

        EntityId sourcePrefab = workspace.World.GetEntityByStableId(sourcePrefabStableId);
        if (sourcePrefab.IsNull || workspace.World.GetNodeType(sourcePrefab) != UiNodeType.Prefab)
        {
            if (instanceIndex >= 0)
            {
                value = values[instanceIndex];
                return true;
            }
            return false;
        }

        if (!workspace.World.TryGetComponent(sourcePrefab, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) || !varsAny.IsValid)
        {
            if (instanceIndex >= 0)
            {
                value = values[instanceIndex];
                return true;
            }
            return false;
        }

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        var vars = PrefabVariablesComponent.Api.FromHandle(workspace.PropertyWorld, varsHandle);
        if (!vars.IsAlive || vars.VariableCount == 0)
        {
            if (instanceIndex >= 0)
            {
                value = values[instanceIndex];
                return true;
            }
            return false;
        }

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

        if (instanceIndex >= 0)
        {
            value = values[instanceIndex];
            return true;
        }

        return false;
    }

    private static void WritePrefabInstanceVariableOverrideNoUndo(UiWorkspace workspace, EntityId instanceEntity, ushort variableId, in PropertyValue value)
    {
        if (!workspace.World.TryGetComponent(instanceEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) || !instanceAny.IsValid)
        {
            return;
        }

        var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
        var instance = PrefabInstanceComponent.Api.FromHandle(workspace.PropertyWorld, instanceHandle);
        if (!instance.IsAlive)
        {
            return;
        }

        ushort count = instance.ValueCount;
        int n = Math.Min(count, (ushort)PrefabInstanceComponent.MaxVariables);
        Span<ushort> ids = instance.VariableIdSpan();
        Span<PropertyValue> values = instance.ValueSpan();
        ulong overrideMask = instance.OverrideMask;

        int index = -1;
        for (int i = 0; i < n; i++)
        {
            if (ids[i] == variableId)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            if (n >= PrefabInstanceComponent.MaxVariables)
            {
                return;
            }

            index = n;
            ids[index] = variableId;
            instance.ValueCount = (ushort)(n + 1);
        }

        values[index] = value;
        instance.OverrideMask = overrideMask | (1UL << index);
    }

    private static bool TryEvaluateTrackAtFrame(AnimationDocument.AnimationTrack track, int frame, out PropertyValue value)
    {
        value = default;

        int keyCount = track.Keys.Count;
        if (keyCount <= 0)
        {
            return false;
        }

        // Keys should already be sorted, but evaluation doesn't require it.
        int firstFrame = track.Keys[0].Frame;
        if (frame <= firstFrame || keyCount == 1)
        {
            value = track.Keys[0].Value;
            return true;
        }

        int lastIndex = keyCount - 1;
        int lastFrame = track.Keys[lastIndex].Frame;
        if (frame >= lastFrame)
        {
            value = track.Keys[lastIndex].Value;
            return true;
        }

        int rightIndex = -1;
        for (int i = 1; i < keyCount; i++)
        {
            if (track.Keys[i].Frame >= frame)
            {
                rightIndex = i;
                break;
            }
        }

        if (rightIndex <= 0)
        {
            value = track.Keys[0].Value;
            return true;
        }

        int leftIndex = rightIndex - 1;
        var a = track.Keys[leftIndex];
        var b = track.Keys[rightIndex];

        if (a.Frame == b.Frame)
        {
            value = a.Value;
            return true;
        }

        float t = (frame - a.Frame) / (float)(b.Frame - a.Frame);
        t = Math.Clamp(t, 0f, 1f);

        // Interpolation is defined on the segment starting at key A.
        if (a.Interpolation == AnimationDocument.Interpolation.Step)
        {
            value = a.Value;
            return true;
        }

        PropertyKind kind = track.Binding.PropertyKind;
        if (kind == PropertyKind.Float)
        {
            float af = a.Value.Float;
            float bf = b.Value.Float;

            float result = a.Interpolation == AnimationDocument.Interpolation.Linear
                ? af + (bf - af) * t
                : HermiteInterpolate(af, a.OutTangent, bf, b.InTangent, t);

            value = PropertyValue.FromFloat(result);
            return true;
        }

        // For non-scalar values, tangents are not defined today. Treat non-step interpolation as linear.
        if (kind == PropertyKind.Vec2)
        {
            Vector2 av = a.Value.Vec2;
            Vector2 bv = b.Value.Vec2;
            value = PropertyValue.FromVec2(av + (bv - av) * t);
            return true;
        }

        if (kind == PropertyKind.Vec3)
        {
            Vector3 av = a.Value.Vec3;
            Vector3 bv = b.Value.Vec3;
            value = PropertyValue.FromVec3(av + (bv - av) * t);
            return true;
        }

        if (kind == PropertyKind.Vec4)
        {
            Vector4 av = a.Value.Vec4;
            Vector4 bv = b.Value.Vec4;
            value = PropertyValue.FromVec4(av + (bv - av) * t);
            return true;
        }

        if (kind == PropertyKind.Color32)
        {
            Color32 ac = a.Value.Color32;
            Color32 bc = b.Value.Color32;
            value = PropertyValue.FromColor32(LerpColor32(ac, bc, t));
            return true;
        }

        value = a.Value;
        return true;
    }

    private static float HermiteInterpolate(float p0, float m0, float p1, float m1, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        float h00 = 2f * t3 - 3f * t2 + 1f;
        float h10 = t3 - 2f * t2 + t;
        float h01 = -2f * t3 + 3f * t2;
        float h11 = t3 - t2;

        return h00 * p0 + h10 * m0 + h01 * p1 + h11 * m1;
    }

    private static Color32 LerpColor32(Color32 a, Color32 b, float t)
    {
        return new Color32(
            LerpByte(a.R, b.R, t),
            LerpByte(a.G, b.G, t),
            LerpByte(a.B, b.B, t),
            LerpByte(a.A, b.A, t));
    }

    private static byte LerpByte(byte a, byte b, float t)
    {
        float v = a + (b - a) * t;
        int vi = (int)MathF.Round(v);
        if (vi < 0)
        {
            vi = 0;
        }
        else if (vi > 255)
        {
            vi = 255;
        }
        return (byte)vi;
    }
}
