using Core;
using Property;
using Property.Runtime;

namespace Derp.UI;

internal static class AnimationTrackRowBuilder
{
    private static readonly string[] ChannelLabels =
    [
        "X",
        "Y",
        "Z",
        "W",
    ];

    public static void Build(UiWorkspace workspace, AnimationEditorState state, AnimationDocument.AnimationTimeline timeline)
    {
        state.Rows.Clear();

        for (int targetIndex = 0; targetIndex < timeline.Targets.Count; targetIndex++)
        {
            var target = timeline.Targets[targetIndex];
            EntityId targetEntity = workspace.World.GetEntityByStableId(target.StableId);

            if (targetEntity.IsNull)
            {
                continue;
            }

            if (!AnimationEditorHelpers.HasAnyKeyedTrackForTarget(timeline, targetIndex))
            {
                continue;
            }

            const int targetDepth = 0;
            const int componentBaseDepth = 1;

            state.Rows.Add(new AnimationEditorState.Row
            {
                Kind = AnimationEditorState.RowKind.TargetHeader,
                Depth = targetDepth,
                Label = string.Empty,
                Entity = targetEntity,
                TargetIndex = targetIndex
            });

            if (!AnimationEditorHelpers.IsTargetCollapsed(workspace, state, targetEntity))
            {
                AddTargetComponentSections(workspace, state, timeline, targetEntity, targetIndex, componentBaseDepth);
            }
        }
    }

    private static void AddTargetComponentSections(UiWorkspace workspace, AnimationEditorState state, AnimationDocument.AnimationTimeline timeline, EntityId entity, int targetIndex, int baseDepth)
    {
        ReadOnlySpan<AnyComponentHandle> slots = workspace.World.GetComponentSlots(entity);
        ulong presentMask = workspace.World.GetComponentPresentMask(entity);
        if (slots.IsEmpty || presentMask == 0)
        {
            return;
        }

        for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            if (((presentMask >> slotIndex) & 1UL) == 0)
            {
                continue;
            }

            AnyComponentHandle component = slots[slotIndex];
            if (component.IsNull)
            {
                continue;
            }

            string label = UiComponentKindNames.GetName(component.Kind);
            AddComponentSection(workspace, state, timeline, entity, targetIndex, label, component, baseDepth: baseDepth);
        }

        AddPrefabInstanceVariableSection(workspace, state, timeline, entity, targetIndex, baseDepth);
    }

    private static void AddComponentSection(UiWorkspace workspace, AnimationEditorState state, AnimationDocument.AnimationTimeline timeline, EntityId entity, int targetIndex, string label, AnyComponentHandle component, int baseDepth)
    {
        int propertyCount = PropertyDispatcher.GetPropertyCount(component);
        if (propertyCount <= 0)
        {
            return;
        }

        if (!ComponentHasAnyKeyedProperties(timeline, targetIndex, component, propertyCount))
        {
            return;
        }

        state.Rows.Add(new AnimationEditorState.Row
        {
            Kind = AnimationEditorState.RowKind.ComponentHeader,
            Depth = baseDepth,
            Label = label,
            TargetIndex = targetIndex,
            Entity = entity,
            ComponentKind = component.Kind
        });

        if (AnimationEditorHelpers.IsComponentCollapsed(workspace, state, entity, component.Kind))
        {
            return;
        }

        AddComponentRows(state, timeline, targetIndex, entity, component, baseDepth: baseDepth + 1);
    }

    private static void AddPrefabInstanceVariableSection(UiWorkspace workspace, AnimationEditorState state, AnimationDocument.AnimationTimeline timeline, EntityId entity, int targetIndex, int baseDepth)
    {
        if (entity.IsNull || workspace.World.GetNodeType(entity) != UiNodeType.PrefabInstance)
        {
            return;
        }

        bool any = false;
        for (int i = 0; i < timeline.Tracks.Count; i++)
        {
            var track = timeline.Tracks[i];
            if (track.Binding.TargetIndex != targetIndex || track.Binding.ComponentKind != PrefabInstanceComponent.Api.PoolIdConst)
            {
                continue;
            }

            if (AnimationBindingIds.TryGetPrefabVariableId(track.Binding.PropertyId, out _))
            {
                any = true;
                break;
            }
        }

        if (!any)
        {
            return;
        }

        const string label = "Variables";
        state.Rows.Add(new AnimationEditorState.Row
        {
            Kind = AnimationEditorState.RowKind.ComponentHeader,
            Depth = baseDepth,
            Label = label,
            TargetIndex = targetIndex,
            Entity = entity,
            ComponentKind = PrefabInstanceComponent.Api.PoolIdConst
        });

        if (AnimationEditorHelpers.IsComponentCollapsed(workspace, state, entity, PrefabInstanceComponent.Api.PoolIdConst))
        {
            return;
        }

        for (int i = 0; i < timeline.Tracks.Count; i++)
        {
            var track = timeline.Tracks[i];
            if (track.Binding.TargetIndex != targetIndex || track.Binding.ComponentKind != PrefabInstanceComponent.Api.PoolIdConst)
            {
                continue;
            }

            if (!AnimationBindingIds.TryGetPrefabVariableId(track.Binding.PropertyId, out ushort variableId))
            {
                continue;
            }

            string rowLabel = TryGetPrefabVariableName(workspace, entity, variableId, out StringHandle nameHandle) && nameHandle.IsValid
                ? nameHandle.ToString()
                : "Var";

            state.Rows.Add(new AnimationEditorState.Row
            {
                Kind = AnimationEditorState.RowKind.Property,
                Depth = baseDepth + 1,
                Label = rowLabel,
                TargetIndex = targetIndex,
                Entity = entity,
                ComponentKind = PrefabInstanceComponent.Api.PoolIdConst,
                PropertyIndexHint = 0,
                PropertyId = track.Binding.PropertyId,
                PropertyKind = track.Binding.PropertyKind,
                ChannelGroupId = 0UL,
                ChannelIndex = 0,
                ChannelCount = 0,
                IsAnimatable = true
            });
        }
    }

    private static bool TryGetPrefabVariableName(UiWorkspace workspace, EntityId instanceEntity, ushort variableId, out StringHandle name)
    {
        name = default;

        if (!workspace.World.TryGetComponent(instanceEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) || !instanceAny.IsValid)
        {
            return false;
        }

        var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
        var instance = PrefabInstanceComponent.Api.FromHandle(workspace.PropertyWorld, instanceHandle);
        if (!instance.IsAlive || instance.SourcePrefabStableId == 0)
        {
            return false;
        }

        EntityId sourcePrefab = workspace.World.GetEntityByStableId(instance.SourcePrefabStableId);
        if (sourcePrefab.IsNull || workspace.World.GetNodeType(sourcePrefab) != UiNodeType.Prefab)
        {
            return false;
        }

        if (!workspace.World.TryGetComponent(sourcePrefab, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) || !varsAny.IsValid)
        {
            return false;
        }

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        var vars = PrefabVariablesComponent.Api.FromHandle(workspace.PropertyWorld, varsHandle);
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
        ReadOnlySpan<StringHandle> names = vars.NameReadOnlySpan();
        for (int i = 0; i < count; i++)
        {
            if (ids[i] == variableId)
            {
                name = names[i];
                return true;
            }
        }

        return false;
    }

    private static bool ComponentHasAnyKeyedProperties(AnimationDocument.AnimationTimeline timeline, int targetIndex, AnyComponentHandle component, int propertyCount)
    {
        for (ushort propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
        {
            if (!PropertyDispatcher.TryGetInfo(component, propertyIndex, out var info))
            {
                continue;
            }

            if (info.HasChannels && !info.IsChannel)
            {
                continue;
            }

            var binding = new AnimationDocument.AnimationBinding(
                targetIndex: targetIndex,
                componentKind: component.Kind,
                propertyIndexHint: propertyIndex,
                propertyId: info.PropertyId,
                propertyKind: info.Kind);

            if (AnimationEditorHelpers.HasKeyedTrack(timeline, binding))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddComponentRows(AnimationEditorState state, AnimationDocument.AnimationTimeline timeline, int targetIndex, EntityId entity, AnyComponentHandle component, int baseDepth)
    {
        int propertyCount = PropertyDispatcher.GetPropertyCount(component);
        if (propertyCount <= 0)
        {
            return;
        }

        ulong activeGroupKey = 0;
        bool activeGroupCollapsed = false;
        ushort activeInlineChannelIndex = ushort.MaxValue;
        ushort activeInlineChannelPropertyIndex = 0;
        ulong activeInlineChannelPropertyId = 0;
        PropertyKind activeInlineChannelKind = PropertyKind.Auto;

        for (ushort propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
        {
            if (!PropertyDispatcher.TryGetInfo(component, propertyIndex, out var info))
            {
                continue;
            }

            if (info.HasChannels && !info.IsChannel)
            {
                bool groupHasKeyedChannels = false;
                ushort bestInlineChannelIndex = ushort.MaxValue;
                ushort bestInlineChannelPropertyIndex = 0;
                ulong bestInlineChannelPropertyId = 0;
                PropertyKind bestInlineChannelKind = PropertyKind.Auto;

                int channelCount = info.ChannelCount;
                int channelsSeen = 0;
                for (int j = propertyIndex + 1; j < propertyCount && channelsSeen < channelCount; j++)
                {
                    if (!PropertyDispatcher.TryGetInfo(component, j, out var channelInfo))
                    {
                        continue;
                    }

                    if (!channelInfo.IsChannel || channelInfo.ChannelGroupId != info.ChannelGroupId)
                    {
                        continue;
                    }

                    channelsSeen++;

                    var channelBinding = new AnimationDocument.AnimationBinding(
                        targetIndex: targetIndex,
                        componentKind: component.Kind,
                        propertyIndexHint: (ushort)j,
                        propertyId: channelInfo.PropertyId,
                        propertyKind: channelInfo.Kind);

                    if (AnimationEditorHelpers.HasKeyedTrack(timeline, channelBinding))
                    {
                        groupHasKeyedChannels = true;
                        if (channelInfo.ChannelIndex < bestInlineChannelIndex)
                        {
                            bestInlineChannelIndex = channelInfo.ChannelIndex;
                            bestInlineChannelPropertyIndex = (ushort)j;
                            bestInlineChannelPropertyId = channelInfo.PropertyId;
                            bestInlineChannelKind = channelInfo.Kind;
                        }
                    }
                }

                if (!groupHasKeyedChannels)
                {
                    continue;
                }

                ulong groupKey = AnimationEditorHelpers.MakeChannelGroupKey(component.Kind, info.ChannelGroupId);
                activeGroupKey = groupKey;
                activeGroupCollapsed = AnimationEditorHelpers.IsGroupCollapsed(state, activeGroupKey);
                activeInlineChannelIndex = bestInlineChannelIndex;
                activeInlineChannelPropertyIndex = bestInlineChannelPropertyIndex;
                activeInlineChannelPropertyId = bestInlineChannelPropertyId;
                activeInlineChannelKind = bestInlineChannelKind;

                state.Rows.Add(new AnimationEditorState.Row
                {
                    Kind = AnimationEditorState.RowKind.PropertyGroup,
                    Depth = baseDepth,
                    Label = info.Name,
                    TargetIndex = targetIndex,
                    Entity = entity,
                    ComponentKind = component.Kind,
                    ChannelGroupId = groupKey,
                    PropertyIndexHint = activeInlineChannelPropertyIndex,
                    PropertyId = activeInlineChannelPropertyId,
                    PropertyKind = activeInlineChannelKind,
                    ChannelIndex = activeInlineChannelIndex,
                    ChannelCount = info.ChannelCount,
                    IsAnimatable = activeInlineChannelIndex != ushort.MaxValue
                });
                continue;
            }

            if (info.IsChannel && activeGroupCollapsed && AnimationEditorHelpers.MakeChannelGroupKey(component.Kind, info.ChannelGroupId) == activeGroupKey)
            {
                continue;
            }

            if (info.HasChannels)
            {
                continue;
            }

            if (info.IsChannel && !activeGroupCollapsed && AnimationEditorHelpers.MakeChannelGroupKey(component.Kind, info.ChannelGroupId) == activeGroupKey)
            {
                if (info.ChannelIndex == activeInlineChannelIndex)
                {
                    continue;
                }
            }

            var binding = new AnimationDocument.AnimationBinding(
                targetIndex: targetIndex,
                componentKind: component.Kind,
                propertyIndexHint: propertyIndex,
                propertyId: info.PropertyId,
                propertyKind: info.Kind);

            if (!AnimationEditorHelpers.HasKeyedTrack(timeline, binding))
            {
                continue;
            }

            state.Rows.Add(new AnimationEditorState.Row
            {
                Kind = AnimationEditorState.RowKind.Property,
                Depth = baseDepth + (info.IsChannel ? 1 : 0),
                Label = info.IsChannel ? GetChannelLabel(info.ChannelIndex, info.Name) : info.Name,
                TargetIndex = targetIndex,
                Entity = entity,
                ComponentKind = component.Kind,
                PropertyIndexHint = propertyIndex,
                PropertyId = info.PropertyId,
                PropertyKind = info.Kind,
                ChannelGroupId = AnimationEditorHelpers.MakeChannelGroupKey(component.Kind, info.ChannelGroupId),
                ChannelIndex = info.ChannelIndex,
                ChannelCount = info.ChannelCount,
                IsAnimatable = !info.HasChannels ? true : info.IsChannel
            });
        }
    }

    private static string GetChannelLabel(ushort channelIndex, string fallback)
    {
        if (channelIndex < ChannelLabels.Length)
        {
            return ChannelLabels[channelIndex];
        }
        return fallback;
    }
}
