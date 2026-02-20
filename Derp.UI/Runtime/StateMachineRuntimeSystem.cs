using System;
using System.Collections.Generic;
using System.Numerics;
using Core;
using Pooled.Runtime;
using Property;
using Property.Runtime;

namespace Derp.UI;

internal static class StateMachineRuntimeSystem
{
    public static void EnsureRuntimeComponents(UiWorkspace workspace)
    {
        UiWorld world = workspace.World;
        World propertyWorld = workspace.PropertyWorld;

        int entityCount = world.EntityCount;
        for (int entityIndex = 1; entityIndex <= entityCount; entityIndex++)
        {
            var entity = new EntityId(entityIndex);
            UiNodeType nodeType = world.GetNodeType(entity);
            if (nodeType == UiNodeType.None)
            {
                continue;
            }

            EntityId definitionOwner = GetDefinitionOwnerForEntity(world, propertyWorld, entity, nodeType);
            if (definitionOwner.IsNull)
            {
                continue;
            }

            if (!world.TryGetComponent(definitionOwner, StateMachineDefinitionComponent.Api.PoolIdConst, out AnyComponentHandle defAny) || !defAny.IsValid)
            {
                continue;
            }

            EnsureRuntimeComponentForEntity(workspace, entity, defAny);
            EnsureTargetResolutionComponentForEntity(workspace, entity);
            EnsureBasePoseComponentForEntity(workspace, entity);
        }
    }

    public static void EnsureRuntimeComponentsScoped(UiWorkspace workspace, List<EntityId> entities)
    {
        UiWorld world = workspace.World;
        World propertyWorld = workspace.PropertyWorld;

        for (int i = 0; i < entities.Count; i++)
        {
            EntityId entity = entities[i];
            UiNodeType nodeType = world.GetNodeType(entity);
            if (nodeType == UiNodeType.None)
            {
                continue;
            }

            EntityId definitionOwner = GetDefinitionOwnerForEntity(world, propertyWorld, entity, nodeType);
            if (definitionOwner.IsNull)
            {
                continue;
            }

            if (!world.TryGetComponent(definitionOwner, StateMachineDefinitionComponent.Api.PoolIdConst, out AnyComponentHandle defAny) || !defAny.IsValid)
            {
                continue;
            }

            EnsureRuntimeComponentForEntity(workspace, entity, defAny);
            EnsureTargetResolutionComponentForEntity(workspace, entity);
            EnsureBasePoseComponentForEntity(workspace, entity);
        }
    }

    public static void Tick(UiWorkspace workspace, uint deltaMicroseconds)
    {
        UiWorld world = workspace.World;
        World propertyWorld = workspace.PropertyWorld;

        int entityCount = world.EntityCount;
        for (int entityIndex = 1; entityIndex <= entityCount; entityIndex++)
        {
            var entity = new EntityId(entityIndex);
            UiNodeType nodeType = world.GetNodeType(entity);
            if (nodeType == UiNodeType.None)
            {
                continue;
            }

            if (!world.TryGetComponent(entity, StateMachineRuntimeComponent.Api.PoolIdConst, out AnyComponentHandle runtimeAny) || !runtimeAny.IsValid)
            {
                continue;
            }

            if (!TryGetOrCreateTargetResolution(workspace, entity, out var targetResolution))
            {
                continue;
            }

            EntityId definitionOwner = GetDefinitionOwnerForEntity(world, propertyWorld, entity, nodeType);
            if (definitionOwner.IsNull)
            {
                continue;
            }

            if (!world.TryGetComponent(definitionOwner, StateMachineDefinitionComponent.Api.PoolIdConst, out AnyComponentHandle defAny) || !defAny.IsValid)
            {
                continue;
            }

            var runtimeHandle = new StateMachineRuntimeComponentHandle(runtimeAny.Index, runtimeAny.Generation);
            var runtime = StateMachineRuntimeComponent.Api.FromHandle(propertyWorld, runtimeHandle);
            if (!runtime.IsAlive)
            {
                continue;
            }

            var defHandle = new StateMachineDefinitionComponentHandle(defAny.Index, defAny.Generation);
            var def = StateMachineDefinitionComponent.Api.FromHandle(propertyWorld, defHandle);
            if (!def.IsAlive)
            {
                continue;
            }

            bool hasLib = TryGetAnimationLibraryForOwner(world, propertyWorld, definitionOwner, out AnimationLibraryComponent.ViewProxy lib);
            if (hasLib)
            {
                EnsureResolvedTargetsUpToDate(workspace, entity, nodeType, definitionOwner, lib, targetResolution);
            }

            PrefabInstanceComponent.ViewProxy instance = default;
            PrefabVariablesComponent.ViewProxy sourcePrefabVars = default;
            bool hasInstance = false;
            bool hasSourcePrefabVars = false;
            if (nodeType == UiNodeType.PrefabInstance)
            {
                if (world.TryGetComponent(entity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) && instanceAny.IsValid)
                {
                    var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
                    instance = PrefabInstanceComponent.Api.FromHandle(propertyWorld, instanceHandle);
                    if (!instance.IsAlive)
                    {
                        instance = default;
                    }
                    else
                    {
                        hasInstance = true;
                    }
                }

                if (world.TryGetComponent(definitionOwner, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) && varsAny.IsValid)
                {
                    var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
                    sourcePrefabVars = PrefabVariablesComponent.Api.FromHandle(propertyWorld, varsHandle);
                    if (!sourcePrefabVars.IsAlive)
                    {
                        sourcePrefabVars = default;
                    }
                    else
                    {
                        hasSourcePrefabVars = true;
                    }
                }
            }
            else
            {
                if (nodeType == UiNodeType.Prefab &&
                    world.TryGetComponent(entity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) &&
                    instanceAny.IsValid)
                {
                    var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
                    instance = PrefabInstanceComponent.Api.FromHandle(propertyWorld, instanceHandle);
                    if (!instance.IsAlive)
                    {
                        instance = default;
                    }
                    else
                    {
                        hasInstance = true;
                    }
                }

                if (world.TryGetComponent(definitionOwner, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) && varsAny.IsValid)
                {
                    var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
                    sourcePrefabVars = PrefabVariablesComponent.Api.FromHandle(propertyWorld, varsHandle);
                    if (!sourcePrefabVars.IsAlive)
                    {
                        sourcePrefabVars = default;
                    }
                    else
                    {
                        hasSourcePrefabVars = true;
                    }
                }
            }

            StepStateMachine(def, runtime, hasInstance, instance, hasSourcePrefabVars, sourcePrefabVars, deltaMicroseconds, filterMachineId: 0);

            if (hasLib)
            {
                if (TryGetOrCreateBasePose(workspace, entity, out AnimationBasePoseRuntimeComponent.ViewProxy basePose))
                {
                    EnsureBasePoseUpToDate(workspace, entity, nodeType, definitionOwner, lib, targetResolution, basePose);
                    ApplyBasePose(workspace, basePose);
                }
                ApplyStateMachineAnimationOutput(workspace, entity, nodeType, def, runtime, lib, targetResolution, hasInstance, instance, hasSourcePrefabVars, sourcePrefabVars, filterMachineId: 0);
            }
        }
    }

    public static void TickScoped(
        UiWorkspace workspace,
        uint deltaMicroseconds,
        List<EntityId> entities,
        uint filterDefinitionOwnerStableId,
        ushort filterMachineId)
    {
        if (deltaMicroseconds == 0 || entities.Count == 0)
        {
            return;
        }

        UiWorld world = workspace.World;
        World propertyWorld = workspace.PropertyWorld;

        for (int listIndex = 0; listIndex < entities.Count; listIndex++)
        {
            EntityId entity = entities[listIndex];
            UiNodeType nodeType = world.GetNodeType(entity);
            if (nodeType == UiNodeType.None)
            {
                continue;
            }

            if (!world.TryGetComponent(entity, StateMachineRuntimeComponent.Api.PoolIdConst, out AnyComponentHandle runtimeAny) || !runtimeAny.IsValid)
            {
                continue;
            }

            if (!TryGetOrCreateTargetResolution(workspace, entity, out var targetResolution))
            {
                continue;
            }

            EntityId definitionOwner = GetDefinitionOwnerForEntity(world, propertyWorld, entity, nodeType);
            if (definitionOwner.IsNull)
            {
                continue;
            }

            if (!world.TryGetComponent(definitionOwner, StateMachineDefinitionComponent.Api.PoolIdConst, out AnyComponentHandle defAny) || !defAny.IsValid)
            {
                continue;
            }

            var runtimeHandle = new StateMachineRuntimeComponentHandle(runtimeAny.Index, runtimeAny.Generation);
            var runtime = StateMachineRuntimeComponent.Api.FromHandle(propertyWorld, runtimeHandle);
            if (!runtime.IsAlive)
            {
                continue;
            }

            var defHandle = new StateMachineDefinitionComponentHandle(defAny.Index, defAny.Generation);
            var def = StateMachineDefinitionComponent.Api.FromHandle(propertyWorld, defHandle);
            if (!def.IsAlive)
            {
                continue;
            }

            bool hasLib = TryGetAnimationLibraryForOwner(world, propertyWorld, definitionOwner, out AnimationLibraryComponent.ViewProxy lib);
            if (hasLib)
            {
                EnsureResolvedTargetsUpToDate(workspace, entity, nodeType, definitionOwner, lib, targetResolution);
            }

            PrefabInstanceComponent.ViewProxy instance = default;
            PrefabVariablesComponent.ViewProxy sourcePrefabVars = default;
            bool hasInstance = false;
            bool hasSourcePrefabVars = false;
            if (nodeType == UiNodeType.PrefabInstance)
            {
                if (world.TryGetComponent(entity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) && instanceAny.IsValid)
                {
                    var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
                    instance = PrefabInstanceComponent.Api.FromHandle(propertyWorld, instanceHandle);
                    if (!instance.IsAlive)
                    {
                        instance = default;
                    }
                    else
                    {
                        hasInstance = true;
                    }
                }

                if (world.TryGetComponent(definitionOwner, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) && varsAny.IsValid)
                {
                    var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
                    sourcePrefabVars = PrefabVariablesComponent.Api.FromHandle(propertyWorld, varsHandle);
                    if (!sourcePrefabVars.IsAlive)
                    {
                        sourcePrefabVars = default;
                    }
                    else
                    {
                        hasSourcePrefabVars = true;
                    }
                }
            }
            else
            {
                if (nodeType == UiNodeType.Prefab &&
                    world.TryGetComponent(entity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) &&
                    instanceAny.IsValid)
                {
                    var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
                    instance = PrefabInstanceComponent.Api.FromHandle(propertyWorld, instanceHandle);
                    if (!instance.IsAlive)
                    {
                        instance = default;
                    }
                    else
                    {
                        hasInstance = true;
                    }
                }

                if (world.TryGetComponent(definitionOwner, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) && varsAny.IsValid)
                {
                    var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
                    sourcePrefabVars = PrefabVariablesComponent.Api.FromHandle(propertyWorld, varsHandle);
                    if (!sourcePrefabVars.IsAlive)
                    {
                        sourcePrefabVars = default;
                    }
                    else
                    {
                        hasSourcePrefabVars = true;
                    }
                }
            }

            ushort localFilterMachineId = 0;
            if (filterMachineId != 0 && filterDefinitionOwnerStableId != 0 && world.GetStableId(definitionOwner) == filterDefinitionOwnerStableId)
            {
                localFilterMachineId = filterMachineId;
            }

            StepStateMachine(def, runtime, hasInstance, instance, hasSourcePrefabVars, sourcePrefabVars, deltaMicroseconds, localFilterMachineId);
            if (hasLib)
            {
                if (TryGetOrCreateBasePose(workspace, entity, out AnimationBasePoseRuntimeComponent.ViewProxy basePose))
                {
                    EnsureBasePoseUpToDate(workspace, entity, nodeType, definitionOwner, lib, targetResolution, basePose);
                    ApplyBasePose(workspace, basePose);
                }
                ApplyStateMachineAnimationOutput(workspace, entity, nodeType, def, runtime, lib, targetResolution, hasInstance, instance, hasSourcePrefabVars, sourcePrefabVars, localFilterMachineId);
            }
        }
    }

    private static EntityId GetDefinitionOwnerForEntity(UiWorld world, World propertyWorld, EntityId entity, UiNodeType nodeType)
    {
        if (nodeType == UiNodeType.Prefab)
        {
            return entity;
        }

        if (nodeType != UiNodeType.PrefabInstance)
        {
            return EntityId.Null;
        }

        if (!world.TryGetComponent(entity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) || !instanceAny.IsValid)
        {
            return EntityId.Null;
        }

        var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
        var instance = PrefabInstanceComponent.Api.FromHandle(propertyWorld, instanceHandle);
        if (!instance.IsAlive || instance.SourcePrefabStableId == 0)
        {
            return EntityId.Null;
        }

        EntityId sourcePrefab = world.GetEntityByStableId(instance.SourcePrefabStableId);
        if (sourcePrefab.IsNull || world.GetNodeType(sourcePrefab) != UiNodeType.Prefab)
        {
            return EntityId.Null;
        }

        return sourcePrefab;
    }

    private static void EnsureRuntimeComponentForEntity(UiWorkspace workspace, EntityId entity, AnyComponentHandle defAny)
    {
        UiWorld world = workspace.World;
        World propertyWorld = workspace.PropertyWorld;

        if (world.TryGetComponent(entity, StateMachineRuntimeComponent.Api.PoolIdConst, out AnyComponentHandle runtimeAny) && runtimeAny.IsValid)
        {
            var runtimeHandle = new StateMachineRuntimeComponentHandle(runtimeAny.Index, runtimeAny.Generation);
            var runtime = StateMachineRuntimeComponent.Api.FromHandle(propertyWorld, runtimeHandle);
            if (runtime.IsAlive)
            {
                return;
            }
        }

        uint stableId = world.GetStableId(entity);
        if (stableId == 0)
        {
            return;
        }

        var defHandle = new StateMachineDefinitionComponentHandle(defAny.Index, defAny.Generation);
        var def = StateMachineDefinitionComponent.Api.FromHandle(propertyWorld, defHandle);
        if (!def.IsAlive)
        {
            return;
        }

        ushort activeMachineId = 0;
        if (def.MachineCount > 0)
        {
            activeMachineId = def.MachineId[0];
        }

        var created = StateMachineRuntimeComponent.Api.CreateWithId(propertyWorld, new StateMachineRuntimeComponentId((ulong)stableId), new StateMachineRuntimeComponent
        {
            ActiveMachineId = activeMachineId,
            IsInitialized = false
        });

        workspace.SetComponentWithStableId(entity, new AnyComponentHandle(StateMachineRuntimeComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation));
    }

    private static void EnsureTargetResolutionComponentForEntity(UiWorkspace workspace, EntityId entity)
    {
        UiWorld world = workspace.World;
        World propertyWorld = workspace.PropertyWorld;

        if (world.TryGetComponent(entity, AnimationTargetResolutionRuntimeComponent.Api.PoolIdConst, out AnyComponentHandle any) && any.IsValid)
        {
            var handle = new AnimationTargetResolutionRuntimeComponentHandle(any.Index, any.Generation);
            var view = AnimationTargetResolutionRuntimeComponent.Api.FromHandle(propertyWorld, handle);
            if (view.IsAlive)
            {
                return;
            }
        }

        uint stableId = world.GetStableId(entity);
        if (stableId == 0)
        {
            return;
        }

        var created = AnimationTargetResolutionRuntimeComponent.Api.CreateWithId(
            propertyWorld,
            new AnimationTargetResolutionRuntimeComponentId((ulong)stableId),
            default(AnimationTargetResolutionRuntimeComponent));

        workspace.SetComponentWithStableId(entity, new AnyComponentHandle(AnimationTargetResolutionRuntimeComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation));
    }

    private static bool TryGetOrCreateTargetResolution(UiWorkspace workspace, EntityId entity, out AnimationTargetResolutionRuntimeComponent.ViewProxy view)
    {
        view = default;

        UiWorld world = workspace.World;
        World propertyWorld = workspace.PropertyWorld;

        if (world.TryGetComponent(entity, AnimationTargetResolutionRuntimeComponent.Api.PoolIdConst, out AnyComponentHandle any) && any.IsValid)
        {
            var handle = new AnimationTargetResolutionRuntimeComponentHandle(any.Index, any.Generation);
            view = AnimationTargetResolutionRuntimeComponent.Api.FromHandle(propertyWorld, handle);
            if (view.IsAlive)
            {
                return true;
            }
        }

        EnsureTargetResolutionComponentForEntity(workspace, entity);

        if (!world.TryGetComponent(entity, AnimationTargetResolutionRuntimeComponent.Api.PoolIdConst, out AnyComponentHandle createdAny) || !createdAny.IsValid)
        {
            return false;
        }

        var createdHandle = new AnimationTargetResolutionRuntimeComponentHandle(createdAny.Index, createdAny.Generation);
        view = AnimationTargetResolutionRuntimeComponent.Api.FromHandle(propertyWorld, createdHandle);
        return view.IsAlive;
    }

    private static void EnsureBasePoseComponentForEntity(UiWorkspace workspace, EntityId entity)
    {
        UiWorld world = workspace.World;
        World propertyWorld = workspace.PropertyWorld;

        if (world.TryGetComponent(entity, AnimationBasePoseRuntimeComponent.Api.PoolIdConst, out AnyComponentHandle any) && any.IsValid)
        {
            var handle = new AnimationBasePoseRuntimeComponentHandle(any.Index, any.Generation);
            var view = AnimationBasePoseRuntimeComponent.Api.FromHandle(propertyWorld, handle);
            if (view.IsAlive)
            {
                return;
            }
        }

        uint stableId = world.GetStableId(entity);
        if (stableId == 0)
        {
            return;
        }

        var created = AnimationBasePoseRuntimeComponent.Api.CreateWithId(
            propertyWorld,
            new AnimationBasePoseRuntimeComponentId((ulong)stableId),
            default(AnimationBasePoseRuntimeComponent));

        workspace.SetComponentWithStableId(entity, new AnyComponentHandle(AnimationBasePoseRuntimeComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation));
    }

    private static bool TryGetOrCreateBasePose(UiWorkspace workspace, EntityId entity, out AnimationBasePoseRuntimeComponent.ViewProxy view)
    {
        view = default;

        UiWorld world = workspace.World;
        World propertyWorld = workspace.PropertyWorld;

        if (world.TryGetComponent(entity, AnimationBasePoseRuntimeComponent.Api.PoolIdConst, out AnyComponentHandle any) && any.IsValid)
        {
            var handle = new AnimationBasePoseRuntimeComponentHandle(any.Index, any.Generation);
            view = AnimationBasePoseRuntimeComponent.Api.FromHandle(propertyWorld, handle);
            if (view.IsAlive)
            {
                return true;
            }
        }

        EnsureBasePoseComponentForEntity(workspace, entity);

        if (!world.TryGetComponent(entity, AnimationBasePoseRuntimeComponent.Api.PoolIdConst, out AnyComponentHandle createdAny) || !createdAny.IsValid)
        {
            return false;
        }

        var createdHandle = new AnimationBasePoseRuntimeComponentHandle(createdAny.Index, createdAny.Generation);
        view = AnimationBasePoseRuntimeComponent.Api.FromHandle(propertyWorld, createdHandle);
        return view.IsAlive;
    }

    private static bool TryGetAnimationLibraryForOwner(UiWorld world, World propertyWorld, EntityId owner, out AnimationLibraryComponent.ViewProxy lib)
    {
        lib = default;
        if (!world.TryGetComponent(owner, AnimationLibraryComponent.Api.PoolIdConst, out AnyComponentHandle any) || !any.IsValid)
        {
            return false;
        }

        var handle = new AnimationLibraryComponentHandle(any.Index, any.Generation);
        lib = AnimationLibraryComponent.Api.FromHandle(propertyWorld, handle);
        return lib.IsAlive;
    }

    private static void EnsureResolvedTargetsUpToDate(
        UiWorkspace workspace,
        EntityId entity,
        UiNodeType nodeType,
        EntityId definitionOwner,
        AnimationLibraryComponent.ViewProxy lib,
        AnimationTargetResolutionRuntimeComponent.ViewProxy targetResolution)
    {
        UiWorld world = workspace.World;
        World propertyWorld = workspace.PropertyWorld;

        uint sourcePrefabStableId = world.GetStableId(definitionOwner);
        uint animRevision = lib.Revision;

        ushort targetCount = lib.TargetCount;
        if (targetCount > AnimationLibraryComponent.MaxTargets)
        {
            targetCount = AnimationLibraryComponent.MaxTargets;
        }

        if (targetResolution.SourcePrefabStableId == sourcePrefabStableId &&
            targetResolution.AnimationLibraryRevision == animRevision &&
            targetResolution.TargetCount == targetCount)
        {
            return;
        }

        targetResolution.SourcePrefabStableId = sourcePrefabStableId;
        targetResolution.AnimationLibraryRevision = animRevision;
        targetResolution.TargetCount = targetCount;

        Span<uint> resolved = targetResolution.ResolvedTargetEntityStableIdSpan();
        for (int i = 0; i < AnimationTargetResolutionRuntimeComponent.MaxTargets; i++)
        {
            resolved[i] = 0;
        }

        ReadOnlySpan<uint> targetStableId = lib.TargetStableIdReadOnlySpan();

        if (nodeType == UiNodeType.Prefab)
        {
            for (int i = 0; i < targetCount; i++)
            {
                resolved[i] = targetStableId[i];
            }
            return;
        }

        if (nodeType != UiNodeType.PrefabInstance)
        {
            return;
        }

        uint instanceRootStableId = world.GetStableId(entity);
        if (instanceRootStableId == 0)
        {
            return;
        }

        Span<uint> sortedSourceStableIds = stackalloc uint[AnimationLibraryComponent.MaxTargets];
        Span<ushort> sortedToTargetIndex = stackalloc ushort[AnimationLibraryComponent.MaxTargets];
        int n = targetCount;
        for (int i = 0; i < n; i++)
        {
            sortedSourceStableIds[i] = targetStableId[i];
            sortedToTargetIndex[i] = (ushort)i;
        }

        InsertionSortByStableId(sortedSourceStableIds.Slice(0, n), sortedToTargetIndex.Slice(0, n));

        int entityCount = world.EntityCount;
        for (int entityIndex = 1; entityIndex <= entityCount; entityIndex++)
        {
            var e = new EntityId(entityIndex);
            if (!world.TryGetComponent(e, PrefabExpandedComponent.Api.PoolIdConst, out AnyComponentHandle expandedAny) || !expandedAny.IsValid)
            {
                continue;
            }

            var expandedHandle = new PrefabExpandedComponentHandle(expandedAny.Index, expandedAny.Generation);
            var expanded = PrefabExpandedComponent.Api.FromHandle(propertyWorld, expandedHandle);
            if (!expanded.IsAlive || expanded.InstanceRootStableId != instanceRootStableId)
            {
                continue;
            }

            uint sourceNodeStableId = expanded.SourceNodeStableId;
            if (sourceNodeStableId == 0)
            {
                continue;
            }

            if (!TryBinarySearch(sortedSourceStableIds.Slice(0, n), sourceNodeStableId, out int sortedIndex))
            {
                continue;
            }

            ushort targetIndex = sortedToTargetIndex[sortedIndex];
            resolved[targetIndex] = world.GetStableId(e);
        }
    }

    private static void EnsureBasePoseUpToDate(
        UiWorkspace workspace,
        EntityId entity,
        UiNodeType nodeType,
        EntityId definitionOwner,
        AnimationLibraryComponent.ViewProxy lib,
        AnimationTargetResolutionRuntimeComponent.ViewProxy targetResolution,
        AnimationBasePoseRuntimeComponent.ViewProxy basePose)
    {
        UiWorld world = workspace.World;
        World registry = workspace.PropertyWorld;

        uint sourcePrefabStableId = world.GetStableId(definitionOwner);
        uint animRevision = lib.Revision;

        if (basePose.SourcePrefabStableId == sourcePrefabStableId &&
            basePose.AnimationLibraryRevision == animRevision)
        {
            return;
        }

        basePose.SourcePrefabStableId = sourcePrefabStableId;
        basePose.AnimationLibraryRevision = animRevision;
        basePose.SlotCount = 0;

        ushort targetCount = targetResolution.TargetCount;
        if (targetCount > AnimationTargetResolutionRuntimeComponent.MaxTargets)
        {
            targetCount = AnimationTargetResolutionRuntimeComponent.MaxTargets;
        }

        int timelineCount = lib.TimelineCount;
        if (timelineCount > AnimationLibraryComponent.MaxTimelines)
        {
            timelineCount = AnimationLibraryComponent.MaxTimelines;
        }

        ushort trackCountTotal = lib.TrackCount;
        if (trackCountTotal > AnimationLibraryComponent.MaxTracks)
        {
            trackCountTotal = AnimationLibraryComponent.MaxTracks;
        }

        ReadOnlySpan<ushort> timelineTargetStart = lib.TimelineTargetStartReadOnlySpan();
        ReadOnlySpan<ushort> timelineTrackStart = lib.TimelineTrackStartReadOnlySpan();
        ReadOnlySpan<byte> timelineTrackCount = lib.TimelineTrackCountReadOnlySpan();

        ReadOnlySpan<ushort> trackTargetIndex = lib.TrackTargetIndexReadOnlySpan();
        ReadOnlySpan<ushort> trackComponentKind = lib.TrackComponentKindReadOnlySpan();
        ReadOnlySpan<ushort> trackPropertyIndexHint = lib.TrackPropertyIndexHintReadOnlySpan();
        ReadOnlySpan<ulong> trackPropertyId = lib.TrackPropertyIdReadOnlySpan();
        ReadOnlySpan<ushort> trackPropertyKind = lib.TrackPropertyKindReadOnlySpan();

        Span<uint> resolvedTargets = targetResolution.ResolvedTargetEntityStableIdSpan();
        Span<PropertySlot> slots = basePose.SlotSpan();
        Span<PropertyValue> baseValues = basePose.BaseValueSpan();

        ushort slotCount = 0;

        for (int timelineSlot = 0; timelineSlot < timelineCount; timelineSlot++)
        {
            ushort targetBase = timelineTargetStart[timelineSlot];
            ushort trackStart = timelineTrackStart[timelineSlot];
            byte trackCount = timelineTrackCount[timelineSlot];

            int trackEnd = trackStart + trackCount;
            if (trackStart >= trackCountTotal || trackEnd > trackCountTotal)
            {
                continue;
            }

            for (int trackIndex = trackStart; trackIndex < trackEnd; trackIndex++)
            {
                ushort componentKind = trackComponentKind[trackIndex];
                if (componentKind == PrefabInstanceComponent.Api.PoolIdConst)
                {
                    continue;
                }

                ushort localTargetIndex = trackTargetIndex[trackIndex];
                uint globalTargetIndexU = (uint)targetBase + localTargetIndex;
                if (globalTargetIndexU >= targetCount)
                {
                    continue;
                }

                ushort targetIndex = (ushort)globalTargetIndexU;
                uint targetStableId = resolvedTargets[targetIndex];
                if (targetStableId == 0)
                {
                    continue;
                }

                EntityId targetEntity = world.GetEntityByStableId(targetStableId);
                if (targetEntity.IsNull)
                {
                    continue;
                }

                if (!world.TryGetComponent(targetEntity, componentKind, out AnyComponentHandle componentAny) || !componentAny.IsValid)
                {
                    continue;
                }

                ulong propertyId = trackPropertyId[trackIndex];
                PropertyKind kind = (PropertyKind)trackPropertyKind[trackIndex];
                ushort hint = trackPropertyIndexHint[trackIndex];

                if (!TryResolvePropertySlot(componentAny, hint, propertyId, kind, out PropertySlot slot))
                {
                    continue;
                }

                if (ContainsSlot(slots, slotCount, slot))
                {
                    continue;
                }

                if (slotCount >= AnimationBasePoseRuntimeComponent.MaxSlots)
                {
                    break;
                }

                PropertyValue baseValue = ReadValue(registry, slot);
                slots[slotCount] = slot;
                baseValues[slotCount] = baseValue;
                slotCount++;
            }

            if (slotCount >= AnimationBasePoseRuntimeComponent.MaxSlots)
            {
                break;
            }
        }

        basePose.SlotCount = slotCount;
    }

    private static bool ContainsSlot(Span<PropertySlot> slots, ushort count, in PropertySlot candidate)
    {
        for (int i = 0; i < count; i++)
        {
            PropertySlot existing = slots[i];
            if (existing.Component.Kind != candidate.Component.Kind ||
                existing.Component.Index != candidate.Component.Index ||
                existing.Component.Generation != candidate.Component.Generation)
            {
                continue;
            }

            if (existing.PropertyIndex != candidate.PropertyIndex)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static PropertyValue ReadValue(World registry, in PropertySlot slot)
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
            PropertyKind.Trigger => PropertyValue.FromBool(PropertyDispatcher.ReadBool(registry, slot)),
            _ => default
        };
    }

    private static void ApplyBasePose(UiWorkspace workspace, AnimationBasePoseRuntimeComponent.ViewProxy basePose)
    {
        ushort count = basePose.SlotCount;
        if (count == 0)
        {
            return;
        }

        if (count > AnimationBasePoseRuntimeComponent.MaxSlots)
        {
            count = AnimationBasePoseRuntimeComponent.MaxSlots;
        }

        World registry = workspace.PropertyWorld;
        ReadOnlySpan<PropertySlot> slots = basePose.SlotReadOnlySpan();
        ReadOnlySpan<PropertyValue> values = basePose.BaseValueReadOnlySpan();

        for (int i = 0; i < count; i++)
        {
            PropertySlot slot = slots[i];
            PropertyKind kind = slot.Kind;
            PropertyValue value = values[i];
            WriteValue(registry, slot, kind, value);
        }
    }

    private static void InsertionSortByStableId(Span<uint> keys, Span<ushort> values)
    {
        for (int i = 1; i < keys.Length; i++)
        {
            uint key = keys[i];
            ushort value = values[i];
            int j = i - 1;
            while (j >= 0 && keys[j] > key)
            {
                keys[j + 1] = keys[j];
                values[j + 1] = values[j];
                j--;
            }
            keys[j + 1] = key;
            values[j + 1] = value;
        }
    }

    private static bool TryBinarySearch(ReadOnlySpan<uint> sortedKeys, uint key, out int index)
    {
        int lo = 0;
        int hi = sortedKeys.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            uint v = sortedKeys[mid];
            if (v == key)
            {
                index = mid;
                return true;
            }
            if (v < key)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        index = -1;
        return false;
    }

    private static void StepStateMachine(
        StateMachineDefinitionComponent.ViewProxy def,
        StateMachineRuntimeComponent.ViewProxy runtime,
        bool hasInstance,
        PrefabInstanceComponent.ViewProxy instance,
        bool hasSourcePrefabVars,
        PrefabVariablesComponent.ViewProxy sourcePrefabVars,
        uint deltaMicroseconds,
        ushort filterMachineId)
    {
        if (!runtime.IsInitialized)
        {
            InitializeAllLayers(def, runtime);
            runtime.IsInitialized = true;
        }

        if (filterMachineId != 0)
        {
            runtime.ActiveMachineId = filterMachineId;
        }
        else if (runtime.ActiveMachineId <= 0 && def.MachineCount > 0)
        {
            runtime.ActiveMachineId = def.MachineId[0];
        }

        ushort debugLayerId = 0;
        ushort debugStateId = 0;
        ushort debugTransitionId = 0;

        Span<ushort> layerCurrentStateId = runtime.LayerCurrentStateIdSpan();
        Span<ushort> layerPreviousStateId = runtime.LayerPreviousStateIdSpan();
        Span<uint> layerStateTimeUs = runtime.LayerStateTimeUsSpan();
        Span<ushort> layerTransitionId = runtime.LayerTransitionIdSpan();
        Span<ushort> layerTransitionFromStateId = runtime.LayerTransitionFromStateIdSpan();
        Span<ushort> layerTransitionToStateId = runtime.LayerTransitionToStateIdSpan();
        Span<uint> layerTransitionTimeUs = runtime.LayerTransitionTimeUsSpan();
        Span<uint> layerTransitionDurationUs = runtime.LayerTransitionDurationUsSpan();

        ushort layerCount = def.LayerCount;
        if (layerCount > StateMachineDefinitionComponent.MaxLayers)
        {
            layerCount = StateMachineDefinitionComponent.MaxLayers;
        }

        ushort machineCount = def.MachineCount;
        if (machineCount > StateMachineDefinitionComponent.MaxMachines)
        {
            machineCount = StateMachineDefinitionComponent.MaxMachines;
        }

        for (int machineSlot = 0; machineSlot < machineCount; machineSlot++)
        {
            ushort machineId = def.MachineId[machineSlot];
            if (machineId == 0)
            {
                continue;
            }

            if (filterMachineId != 0 && machineId != filterMachineId)
            {
                continue;
            }

            for (int layerSlot = 0; layerSlot < layerCount; layerSlot++)
            {
                if (def.LayerMachineId[layerSlot] != machineId)
                {
                    continue;
                }

                ushort layerId = def.LayerId[layerSlot];
                if (layerId == 0)
                {
                    continue;
                }

                if (debugLayerId == 0)
                {
                    debugLayerId = layerId;
                }

                uint stateTime = layerStateTimeUs[layerSlot];
                uint newStateTime = stateTime + deltaMicroseconds;
                if (newStateTime < stateTime)
                {
                    newStateTime = uint.MaxValue;
                }
                layerStateTimeUs[layerSlot] = newStateTime;

                ushort activeTransitionId = layerTransitionId[layerSlot];
                if (activeTransitionId != 0)
                {
                    uint durationUs = layerTransitionDurationUs[layerSlot];
                    if (durationUs == 0)
                    {
                        layerTransitionId[layerSlot] = 0;
                        layerTransitionFromStateId[layerSlot] = 0;
                        layerTransitionToStateId[layerSlot] = 0;
                        layerTransitionTimeUs[layerSlot] = 0;
                        layerTransitionDurationUs[layerSlot] = 0;
                    }
                    else
                    {
                        uint timeUs = layerTransitionTimeUs[layerSlot];
                        uint newTime = timeUs + deltaMicroseconds;
                        if (newTime < timeUs)
                        {
                            newTime = uint.MaxValue;
                        }

                        if (newTime >= durationUs)
                        {
                            layerTransitionId[layerSlot] = 0;
                            layerTransitionFromStateId[layerSlot] = 0;
                            layerTransitionToStateId[layerSlot] = 0;
                            layerTransitionTimeUs[layerSlot] = 0;
                            layerTransitionDurationUs[layerSlot] = 0;
                        }
                        else
                        {
                            layerTransitionTimeUs[layerSlot] = newTime;
                        }
                    }
                }

                ushort currentStateId = layerCurrentStateId[layerSlot];
                if (debugStateId == 0)
                {
                    debugStateId = currentStateId;
                }

                if (TrySelectTransition(def, runtime, layerId, layerSlot, currentStateId, hasInstance, instance, hasSourcePrefabVars, sourcePrefabVars, out ushort transitionId, out ushort nextStateId, out int transitionSlot))
                {
                    layerPreviousStateId[layerSlot] = currentStateId;
                    layerCurrentStateId[layerSlot] = nextStateId;
                    layerStateTimeUs[layerSlot] = 0;
                    debugTransitionId = transitionId;

                    if (transitionSlot >= 0)
                    {
                        float durationSeconds = def.TransitionDurationSeconds[transitionSlot];
                        if (float.IsFinite(durationSeconds) && durationSeconds > 0f)
                        {
                            uint durationUs = (uint)Math.Clamp((int)MathF.Round(durationSeconds * 1_000_000f), 0, int.MaxValue);
                            if (durationUs > 0)
                            {
                                layerTransitionId[layerSlot] = transitionId;
                                layerTransitionFromStateId[layerSlot] = currentStateId;
                                layerTransitionToStateId[layerSlot] = nextStateId;
                                layerTransitionTimeUs[layerSlot] = 0;
                                layerTransitionDurationUs[layerSlot] = durationUs;
                            }
                        }
                    }

                    if (debugStateId != 0 && layerId == debugLayerId)
                    {
                        debugStateId = nextStateId;
                    }
                }
            }
        }

        runtime.DebugActiveLayerId = debugLayerId;
        runtime.DebugActiveStateId = debugStateId;
        runtime.DebugLastTransitionId = debugTransitionId;
    }

    private static void InitializeAllLayers(StateMachineDefinitionComponent.ViewProxy def, StateMachineRuntimeComponent.ViewProxy runtime)
    {
        Span<ushort> layerCurrentStateId = runtime.LayerCurrentStateIdSpan();
        Span<ushort> layerPreviousStateId = runtime.LayerPreviousStateIdSpan();
        Span<uint> layerStateTimeUs = runtime.LayerStateTimeUsSpan();
        Span<ushort> layerTransitionId = runtime.LayerTransitionIdSpan();
        Span<ushort> layerTransitionFromStateId = runtime.LayerTransitionFromStateIdSpan();
        Span<ushort> layerTransitionToStateId = runtime.LayerTransitionToStateIdSpan();
        Span<uint> layerTransitionTimeUs = runtime.LayerTransitionTimeUsSpan();
        Span<uint> layerTransitionDurationUs = runtime.LayerTransitionDurationUsSpan();

        ushort layerCount = def.LayerCount;
        if (layerCount > StateMachineDefinitionComponent.MaxLayers)
        {
            layerCount = StateMachineDefinitionComponent.MaxLayers;
        }

        for (int layerSlot = 0; layerSlot < layerCount; layerSlot++)
        {
            ushort entryTarget = def.LayerEntryTargetStateId[layerSlot];
            layerCurrentStateId[layerSlot] = entryTarget;
            layerPreviousStateId[layerSlot] = 0;
            layerStateTimeUs[layerSlot] = 0;
            layerTransitionId[layerSlot] = 0;
            layerTransitionFromStateId[layerSlot] = 0;
            layerTransitionToStateId[layerSlot] = 0;
            layerTransitionTimeUs[layerSlot] = 0;
            layerTransitionDurationUs[layerSlot] = 0;
        }
    }

    private static void ApplyStateMachineAnimationOutput(
        UiWorkspace workspace,
        EntityId entity,
        UiNodeType nodeType,
        StateMachineDefinitionComponent.ViewProxy def,
        StateMachineRuntimeComponent.ViewProxy runtime,
        AnimationLibraryComponent.ViewProxy lib,
        AnimationTargetResolutionRuntimeComponent.ViewProxy targetResolution,
        bool hasInstance,
        PrefabInstanceComponent.ViewProxy instance,
        bool hasSourcePrefabVars,
        PrefabVariablesComponent.ViewProxy sourcePrefabVars,
        ushort filterMachineId)
    {
        Span<ushort> layerCurrentStateId = runtime.LayerCurrentStateIdSpan();
        Span<uint> layerStateTimeUs = runtime.LayerStateTimeUsSpan();

        ushort layerCount = def.LayerCount;
        if (layerCount > StateMachineDefinitionComponent.MaxLayers)
        {
            layerCount = StateMachineDefinitionComponent.MaxLayers;
        }

        ushort machineCount = def.MachineCount;
        if (machineCount > StateMachineDefinitionComponent.MaxMachines)
        {
            machineCount = StateMachineDefinitionComponent.MaxMachines;
        }

        for (int machineSlot = 0; machineSlot < machineCount; machineSlot++)
        {
            ushort machineId = def.MachineId[machineSlot];
            if (machineId == 0)
            {
                continue;
            }

            if (filterMachineId != 0 && machineId != filterMachineId)
            {
                continue;
            }

            for (int layerSlot = 0; layerSlot < layerCount; layerSlot++)
            {
                if (def.LayerMachineId[layerSlot] != machineId)
                {
                    continue;
                }

                ushort layerId = def.LayerId[layerSlot];
                if (layerId == 0)
                {
                    continue;
                }

                ushort stateId = layerCurrentStateId[layerSlot];
                if (stateId == 0)
                {
                    continue;
                }

                if (!TryFindStateSlot(def, layerId, stateId, out int stateSlot))
                {
                    continue;
                }

                var kind = (StateMachineDefinitionComponent.StateKind)def.StateKindValue[stateSlot];
                uint stateTimeUs = layerStateTimeUs[layerSlot];

                if (kind == StateMachineDefinitionComponent.StateKind.Timeline)
                {
                    int timelineId = def.StateTimelineId[stateSlot];
                    if (!TryFindTimelineSlot(lib, timelineId, out int timelineSlot))
                    {
                        continue;
                    }

                    float speed = def.StatePlaybackSpeed[stateSlot];
                    if (!float.IsFinite(speed) || speed <= 0f)
                    {
                        speed = 1f;
                    }

                    int frame = ComputeTimelineFrame(lib, timelineSlot, stateTimeUs, speed);
                    ApplyTimelineAtFrame(workspace, entity, nodeType, lib, targetResolution, timelineSlot, frame);
                    continue;
                }

                if (kind == StateMachineDefinitionComponent.StateKind.Blend1D)
                {
                    ApplyBlend1DAtTime(workspace, entity, nodeType, def, stateSlot, stateTimeUs, runtime, lib, targetResolution, hasInstance, instance, hasSourcePrefabVars, sourcePrefabVars);
                }
            }
        }
    }

    private static bool TryFindStateSlot(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, ushort stateId, out int slot)
    {
        slot = -1;
        int count = def.StateCount;
        if (count > StateMachineDefinitionComponent.MaxStates)
        {
            count = StateMachineDefinitionComponent.MaxStates;
        }

        for (int i = 0; i < count; i++)
        {
            if (def.StateLayerId[i] != layerId)
            {
                continue;
            }
            if (def.StateId[i] == stateId)
            {
                slot = i;
                return true;
            }
        }

        return false;
    }

    private static bool TryFindTimelineSlot(AnimationLibraryComponent.ViewProxy lib, int timelineId, out int slot)
    {
        slot = -1;
        if (timelineId <= 0)
        {
            return false;
        }

        int count = lib.TimelineCount;
        if (count > AnimationLibraryComponent.MaxTimelines)
        {
            count = AnimationLibraryComponent.MaxTimelines;
        }

        ReadOnlySpan<int> ids = lib.TimelineIdReadOnlySpan();
        for (int i = 0; i < count; i++)
        {
            if (ids[i] == timelineId)
            {
                slot = i;
                return true;
            }
        }

        return false;
    }

    private static int ComputeTimelineFrame(AnimationLibraryComponent.ViewProxy lib, int timelineSlot, uint stateTimeUs, float stateSpeed)
    {
        int duration = lib.TimelineDurationFrames[timelineSlot];
        if (duration <= 0)
        {
            duration = 1;
        }

        int rangeStart = lib.TimelineWorkStartFrame[timelineSlot];
        int rangeEnd = lib.TimelineWorkEndFrame[timelineSlot];
        rangeStart = Math.Clamp(rangeStart, 0, duration);
        rangeEnd = Math.Clamp(rangeEnd, 0, duration);
        if (rangeEnd < rangeStart)
        {
            (rangeStart, rangeEnd) = (rangeEnd, rangeStart);
        }

        if (rangeEnd == rangeStart)
        {
            return rangeStart;
        }

        int fps = lib.TimelineSnapFps[timelineSlot];
        if (fps <= 0)
        {
            fps = 60;
        }

        float timelineSpeed = lib.TimelinePlaybackSpeed[timelineSlot];
        if (!float.IsFinite(timelineSpeed) || timelineSpeed <= 0f)
        {
            timelineSpeed = 1f;
        }

        float speed = stateSpeed * timelineSpeed;
        if (!float.IsFinite(speed) || speed <= 0f)
        {
            speed = 1f;
        }

        float stepsF = stateTimeUs * (fps * speed) / 1_000_000f;
        if (!float.IsFinite(stepsF) || stepsF < 0f)
        {
            stepsF = 0f;
        }

        int steps = (int)stepsF;

        var mode = (AnimationLibraryComponent.PlaybackMode)lib.TimelinePlaybackModeValue[timelineSlot];
        int len = rangeEnd - rangeStart;
        int cycle = len + 1;

        if (mode == AnimationLibraryComponent.PlaybackMode.OneShot)
        {
            int frame = rangeStart + steps;
            if (frame >= rangeEnd)
            {
                return rangeEnd;
            }
            return frame;
        }

        if (mode == AnimationLibraryComponent.PlaybackMode.Loop)
        {
            if (cycle <= 0)
            {
                return rangeStart;
            }
            int pos = steps % cycle;
            if (pos < 0)
            {
                pos += cycle;
            }
            return rangeStart + pos;
        }

        // PingPong
        int pingLen = len * 2;
        if (pingLen <= 0)
        {
            return rangeStart;
        }
        int pingPos = steps % pingLen;
        if (pingPos < 0)
        {
            pingPos += pingLen;
        }
        if (pingPos <= len)
        {
            return rangeStart + pingPos;
        }
        return rangeEnd - (pingPos - len);
    }

    private static void ApplyTimelineAtFrame(
        UiWorkspace workspace,
        EntityId entity,
        UiNodeType nodeType,
        AnimationLibraryComponent.ViewProxy lib,
        AnimationTargetResolutionRuntimeComponent.ViewProxy targetResolution,
        int timelineSlot,
        int frame)
    {
        ReadOnlySpan<ushort> timelineTargetStart = lib.TimelineTargetStartReadOnlySpan();
        ReadOnlySpan<ushort> timelineTrackStart = lib.TimelineTrackStartReadOnlySpan();
        ReadOnlySpan<byte> timelineTrackCount = lib.TimelineTrackCountReadOnlySpan();

        ushort targetBase = timelineTargetStart[timelineSlot];
        ushort trackStart = timelineTrackStart[timelineSlot];
        byte trackCount = timelineTrackCount[timelineSlot];

        ushort trackCountTotal = lib.TrackCount;
        if (trackCountTotal > AnimationLibraryComponent.MaxTracks)
        {
            trackCountTotal = AnimationLibraryComponent.MaxTracks;
        }

        int end = trackStart + trackCount;
        if (trackStart >= trackCountTotal || end > trackCountTotal)
        {
            return;
        }

        ReadOnlySpan<ushort> trackTargetIndex = lib.TrackTargetIndexReadOnlySpan();
        ReadOnlySpan<ushort> trackComponentKind = lib.TrackComponentKindReadOnlySpan();
        ReadOnlySpan<ushort> trackPropertyIndexHint = lib.TrackPropertyIndexHintReadOnlySpan();
        ReadOnlySpan<ulong> trackPropertyId = lib.TrackPropertyIdReadOnlySpan();
        ReadOnlySpan<ushort> trackPropertyKind = lib.TrackPropertyKindReadOnlySpan();
        ReadOnlySpan<ushort> trackKeyStart = lib.TrackKeyStartReadOnlySpan();
        ReadOnlySpan<ushort> trackKeyCount = lib.TrackKeyCountReadOnlySpan();

        ReadOnlySpan<int> keyFrame = lib.KeyFrameReadOnlySpan();
        ReadOnlySpan<PropertyValue> keyValue = lib.KeyValueReadOnlySpan();
        ReadOnlySpan<byte> keyInterpolationValue = lib.KeyInterpolationValueReadOnlySpan();
        ReadOnlySpan<float> keyInTangent = lib.KeyInTangentReadOnlySpan();
        ReadOnlySpan<float> keyOutTangent = lib.KeyOutTangentReadOnlySpan();

        Span<uint> resolvedTargets = targetResolution.ResolvedTargetEntityStableIdSpan();
        UiWorld world = workspace.World;
        World registry = workspace.PropertyWorld;

        for (int trackIndex = trackStart; trackIndex < end; trackIndex++)
        {
            ushort localTargetIndex = trackTargetIndex[trackIndex];
            uint globalTargetIndexU = (uint)targetBase + localTargetIndex;
            if (globalTargetIndexU >= targetResolution.TargetCount)
            {
                continue;
            }

            ushort targetIndex = (ushort)globalTargetIndexU;
            uint targetStableId = resolvedTargets[targetIndex];
            if (targetStableId == 0)
            {
                continue;
            }

            EntityId targetEntity = world.GetEntityByStableId(targetStableId);
            if (targetEntity.IsNull)
            {
                continue;
            }

            ulong propertyId = trackPropertyId[trackIndex];
            PropertyKind kind = (PropertyKind)trackPropertyKind[trackIndex];

            ushort keyStart = trackKeyStart[trackIndex];
            ushort keyCount = trackKeyCount[trackIndex];

            if (!TryEvaluateKeysAtFrame(keyFrame, keyValue, keyInterpolationValue, keyInTangent, keyOutTangent, keyStart, keyCount, frame, kind, out PropertyValue value))
            {
                continue;
            }

            ushort componentKind = trackComponentKind[trackIndex];
            if (componentKind == PrefabInstanceComponent.Api.PoolIdConst && AnimationBindingIds.TryGetPrefabVariableId(propertyId, out ushort variableId))
            {
                WritePrefabInstanceVariableOverrideNoUndo(workspace, targetEntity, variableId, value);
                continue;
            }

            if (!world.TryGetComponent(targetEntity, componentKind, out AnyComponentHandle componentAny) || !componentAny.IsValid)
            {
                continue;
            }

            ushort hint = trackPropertyIndexHint[trackIndex];
            if (!TryResolvePropertySlot(componentAny, hint, propertyId, kind, out PropertySlot slot))
            {
                continue;
            }

            WriteValue(registry, slot, kind, value);
        }
    }

    private static void ApplyBlend1DAtTime(
        UiWorkspace workspace,
        EntityId entity,
        UiNodeType nodeType,
        StateMachineDefinitionComponent.ViewProxy def,
        int stateSlot,
        uint stateTimeUs,
        StateMachineRuntimeComponent.ViewProxy runtime,
        AnimationLibraryComponent.ViewProxy lib,
        AnimationTargetResolutionRuntimeComponent.ViewProxy targetResolution,
        bool hasInstance,
        PrefabInstanceComponent.ViewProxy instance,
        bool hasSourcePrefabVars,
        PrefabVariablesComponent.ViewProxy sourcePrefabVars)
    {
        ushort paramVarId = def.StateBlendParameterVariableId[stateSlot];
        float paramValue = 0f;

        if (paramVarId != 0 &&
            TryGetVariableValueAndKind(hasInstance, instance, hasSourcePrefabVars, sourcePrefabVars, paramVarId, out PropertyKind paramKind, out PropertyValue current))
        {
            paramValue = paramKind switch
            {
                PropertyKind.Float => current.Float,
                PropertyKind.Int => current.Int,
                PropertyKind.Fixed64 => current.Fixed64.ToFloat(),
                _ => 0f
            };
        }

        ushort start = def.StateBlendChildStart[stateSlot];
        byte count = def.StateBlendChildCount[stateSlot];
        if (count == 0 || def.BlendChildCount == 0)
        {
            return;
        }

        ushort total = def.BlendChildCount;
        uint endU = (uint)start + count;
        if (start >= total)
        {
            return;
        }
        if (endU > total)
        {
            count = (byte)(total - start);
        }

        ReadOnlySpan<float> thresholds = def.BlendChildThresholdReadOnlySpan();
        ReadOnlySpan<int> timelineIds = def.BlendChildTimelineIdReadOnlySpan();

        int leftIndex = -1;
        int rightIndex = -1;
        float leftThreshold = float.NegativeInfinity;
        float rightThreshold = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            int slot = start + i;
            float th = thresholds[slot];
            if (!float.IsFinite(th))
            {
                continue;
            }

            if (th <= paramValue && th >= leftThreshold)
            {
                leftThreshold = th;
                leftIndex = slot;
            }

            if (th >= paramValue && th <= rightThreshold)
            {
                rightThreshold = th;
                rightIndex = slot;
            }
        }

        if (leftIndex < 0 && rightIndex < 0)
        {
            return;
        }

        if (leftIndex < 0)
        {
            leftIndex = rightIndex;
            leftThreshold = rightThreshold;
        }

        if (rightIndex < 0)
        {
            rightIndex = leftIndex;
            rightThreshold = leftThreshold;
        }

        int leftTimelineId = timelineIds[leftIndex];
        int rightTimelineId = timelineIds[rightIndex];

        if (!TryFindTimelineSlot(lib, leftTimelineId, out int leftTimelineSlot))
        {
            return;
        }

        float speed = def.StatePlaybackSpeed[stateSlot];
        if (!float.IsFinite(speed) || speed <= 0f)
        {
            speed = 1f;
        }

        int leftFrame = ComputeTimelineFrame(lib, leftTimelineSlot, stateTimeUs, speed);

        if (leftTimelineId == rightTimelineId || rightIndex == leftIndex)
        {
            ApplyTimelineAtFrame(workspace, entity, nodeType, lib, targetResolution, leftTimelineSlot, leftFrame);
            return;
        }

        if (!TryFindTimelineSlot(lib, rightTimelineId, out int rightTimelineSlot))
        {
            ApplyTimelineAtFrame(workspace, entity, nodeType, lib, targetResolution, leftTimelineSlot, leftFrame);
            return;
        }

        int rightFrame = ComputeTimelineFrame(lib, rightTimelineSlot, stateTimeUs, speed);

        float t = 0f;
        if (float.IsFinite(leftThreshold) && float.IsFinite(rightThreshold) && rightThreshold != leftThreshold)
        {
            t = (paramValue - leftThreshold) / (rightThreshold - leftThreshold);
            t = Math.Clamp(t, 0f, 1f);
        }

        ApplyBlendBetweenTimelines(workspace, entity, nodeType, lib, targetResolution, leftTimelineSlot, leftFrame, rightTimelineSlot, rightFrame, t);
    }

    private static void ApplyBlendBetweenTimelines(
        UiWorkspace workspace,
        EntityId entity,
        UiNodeType nodeType,
        AnimationLibraryComponent.ViewProxy lib,
        AnimationTargetResolutionRuntimeComponent.ViewProxy targetResolution,
        int timelineSlotA,
        int frameA,
        int timelineSlotB,
        int frameB,
        float t)
    {
        ReadOnlySpan<ushort> timelineTrackStart = lib.TimelineTrackStartReadOnlySpan();
        ReadOnlySpan<byte> timelineTrackCount = lib.TimelineTrackCountReadOnlySpan();

        ushort startA = timelineTrackStart[timelineSlotA];
        byte countA = timelineTrackCount[timelineSlotA];
        ushort startB = timelineTrackStart[timelineSlotB];
        byte countB = timelineTrackCount[timelineSlotB];

        ushort trackCountTotal = lib.TrackCount;
        if (trackCountTotal > AnimationLibraryComponent.MaxTracks)
        {
            trackCountTotal = AnimationLibraryComponent.MaxTracks;
        }

        int endA = startA + countA;
        int endB = startB + countB;
        if (startA >= trackCountTotal || endA > trackCountTotal)
        {
            return;
        }
        if (startB >= trackCountTotal || endB > trackCountTotal)
        {
            ApplyTimelineAtFrame(workspace, entity, nodeType, lib, targetResolution, timelineSlotA, frameA);
            return;
        }

        ReadOnlySpan<ushort> trackTargetIndex = lib.TrackTargetIndexReadOnlySpan();
        ReadOnlySpan<ushort> trackComponentKind = lib.TrackComponentKindReadOnlySpan();
        ReadOnlySpan<ushort> trackPropertyIndexHint = lib.TrackPropertyIndexHintReadOnlySpan();
        ReadOnlySpan<ulong> trackPropertyId = lib.TrackPropertyIdReadOnlySpan();
        ReadOnlySpan<ushort> trackPropertyKind = lib.TrackPropertyKindReadOnlySpan();
        ReadOnlySpan<ushort> trackKeyStart = lib.TrackKeyStartReadOnlySpan();
        ReadOnlySpan<ushort> trackKeyCount = lib.TrackKeyCountReadOnlySpan();

        ReadOnlySpan<int> keyFrame = lib.KeyFrameReadOnlySpan();
        ReadOnlySpan<PropertyValue> keyValue = lib.KeyValueReadOnlySpan();
        ReadOnlySpan<byte> keyInterpolationValue = lib.KeyInterpolationValueReadOnlySpan();
        ReadOnlySpan<float> keyInTangent = lib.KeyInTangentReadOnlySpan();
        ReadOnlySpan<float> keyOutTangent = lib.KeyOutTangentReadOnlySpan();

        Span<uint> resolvedTargets = targetResolution.ResolvedTargetEntityStableIdSpan();
        UiWorld world = workspace.World;
        World registry = workspace.PropertyWorld;

        for (int trackA = startA; trackA < endA; trackA++)
        {
            if (!TryEvaluateKeysAtFrame(keyFrame, keyValue, keyInterpolationValue, keyInTangent, keyOutTangent, trackKeyStart[trackA], trackKeyCount[trackA], frameA, (PropertyKind)trackPropertyKind[trackA], out PropertyValue valueA))
            {
                continue;
            }

            ushort targetIndex = trackTargetIndex[trackA];
            if (targetIndex >= targetResolution.TargetCount)
            {
                continue;
            }

            uint targetStableId = resolvedTargets[targetIndex];
            if (targetStableId == 0)
            {
                continue;
            }

            EntityId targetEntity = world.GetEntityByStableId(targetStableId);
            if (targetEntity.IsNull)
            {
                continue;
            }

            ulong propertyId = trackPropertyId[trackA];
            PropertyKind kind = (PropertyKind)trackPropertyKind[trackA];
            ushort componentKind = trackComponentKind[trackA];

            int matchB = FindMatchingTrack(trackA, startB, endB, trackTargetIndex, trackComponentKind, trackPropertyId, trackPropertyKind);
            PropertyValue blended = valueA;

            if (matchB >= 0 &&
                TryEvaluateKeysAtFrame(keyFrame, keyValue, keyInterpolationValue, keyInTangent, keyOutTangent, trackKeyStart[matchB], trackKeyCount[matchB], frameB, kind, out PropertyValue valueB) &&
                TryBlendValue(kind, valueA, valueB, t, out PropertyValue result))
            {
                blended = result;
            }

            if (componentKind == PrefabInstanceComponent.Api.PoolIdConst && AnimationBindingIds.TryGetPrefabVariableId(propertyId, out ushort variableId))
            {
                WritePrefabInstanceVariableOverrideNoUndo(workspace, targetEntity, variableId, blended);
                continue;
            }

            if (!world.TryGetComponent(targetEntity, componentKind, out AnyComponentHandle componentAny) || !componentAny.IsValid)
            {
                continue;
            }

            ushort hint = trackPropertyIndexHint[trackA];
            if (!TryResolvePropertySlot(componentAny, hint, propertyId, kind, out PropertySlot slot))
            {
                if (matchB >= 0)
                {
                    ushort hintB = trackPropertyIndexHint[matchB];
                    if (!TryResolvePropertySlot(componentAny, hintB, propertyId, kind, out slot))
                    {
                        continue;
                    }
                }
                else
                {
                    continue;
                }
            }

            WriteValue(registry, slot, kind, blended);
        }

        // Apply tracks that exist only in B.
        for (int trackB = startB; trackB < endB; trackB++)
        {
            if (FindMatchingTrack(trackB, startA, endA, trackTargetIndex, trackComponentKind, trackPropertyId, trackPropertyKind) >= 0)
            {
                continue;
            }

            PropertyKind kind = (PropertyKind)trackPropertyKind[trackB];
            if (!TryEvaluateKeysAtFrame(keyFrame, keyValue, keyInterpolationValue, keyInTangent, keyOutTangent, trackKeyStart[trackB], trackKeyCount[trackB], frameB, kind, out PropertyValue valueB))
            {
                continue;
            }

            ushort targetIndex = trackTargetIndex[trackB];
            if (targetIndex >= targetResolution.TargetCount)
            {
                continue;
            }

            uint targetStableId = resolvedTargets[targetIndex];
            if (targetStableId == 0)
            {
                continue;
            }

            EntityId targetEntity = world.GetEntityByStableId(targetStableId);
            if (targetEntity.IsNull)
            {
                continue;
            }

            ulong propertyId = trackPropertyId[trackB];
            ushort componentKind = trackComponentKind[trackB];

            if (componentKind == PrefabInstanceComponent.Api.PoolIdConst && AnimationBindingIds.TryGetPrefabVariableId(propertyId, out ushort variableId))
            {
                WritePrefabInstanceVariableOverrideNoUndo(workspace, targetEntity, variableId, valueB);
                continue;
            }

            if (!world.TryGetComponent(targetEntity, componentKind, out AnyComponentHandle componentAny) || !componentAny.IsValid)
            {
                continue;
            }

            ushort hint = trackPropertyIndexHint[trackB];
            if (!TryResolvePropertySlot(componentAny, hint, propertyId, kind, out PropertySlot slot))
            {
                continue;
            }

            WriteValue(registry, slot, kind, valueB);
        }
    }

    private static int FindMatchingTrack(
        int trackIndex,
        int startOther,
        int endOther,
        ReadOnlySpan<ushort> trackTargetIndex,
        ReadOnlySpan<ushort> trackComponentKind,
        ReadOnlySpan<ulong> trackPropertyId,
        ReadOnlySpan<ushort> trackPropertyKind)
    {
        ushort targetIndex = trackTargetIndex[trackIndex];
        ushort componentKind = trackComponentKind[trackIndex];
        ulong propertyId = trackPropertyId[trackIndex];
        ushort kind = trackPropertyKind[trackIndex];

        for (int i = startOther; i < endOther; i++)
        {
            if (trackTargetIndex[i] != targetIndex)
            {
                continue;
            }
            if (trackComponentKind[i] != componentKind)
            {
                continue;
            }
            if (trackPropertyKind[i] != kind)
            {
                continue;
            }
            if (trackPropertyId[i] != propertyId)
            {
                continue;
            }
            return i;
        }

        return -1;
    }

    private static bool TryEvaluateKeysAtFrame(
        ReadOnlySpan<int> keyFrame,
        ReadOnlySpan<PropertyValue> keyValue,
        ReadOnlySpan<byte> keyInterpolationValue,
        ReadOnlySpan<float> keyInTangent,
        ReadOnlySpan<float> keyOutTangent,
        ushort keyStart,
        ushort keyCount,
        int frame,
        PropertyKind kind,
        out PropertyValue value)
    {
        value = default;

        if (keyCount == 0)
        {
            return false;
        }

        ushort keyCountTotal = (ushort)keyFrame.Length;
        uint startU = keyStart;
        uint endU = (uint)keyStart + keyCount;
        if (startU >= keyCountTotal || endU > keyCountTotal)
        {
            return false;
        }

        int start = keyStart;
        int end = keyStart + keyCount;

        int firstFrame = keyFrame[start];
        if (frame <= firstFrame || keyCount == 1)
        {
            value = keyValue[start];
            return true;
        }

        int lastIndex = end - 1;
        int lastFrame = keyFrame[lastIndex];
        if (frame >= lastFrame)
        {
            value = keyValue[lastIndex];
            return true;
        }

        int rightIndex = -1;
        for (int i = start + 1; i < end; i++)
        {
            if (keyFrame[i] >= frame)
            {
                rightIndex = i;
                break;
            }
        }

        if (rightIndex <= start)
        {
            value = keyValue[start];
            return true;
        }

        int leftIndex = rightIndex - 1;
        int aframe = keyFrame[leftIndex];
        int bframe = keyFrame[rightIndex];
        if (aframe == bframe)
        {
            value = keyValue[leftIndex];
            return true;
        }

        float t = (frame - aframe) / (float)(bframe - aframe);
        t = Math.Clamp(t, 0f, 1f);

        byte interp = keyInterpolationValue[leftIndex];
        if (interp == (byte)AnimationLibraryComponent.Interpolation.Step)
        {
            value = keyValue[leftIndex];
            return true;
        }

        if (kind == PropertyKind.Float)
        {
            float a = keyValue[leftIndex].Float;
            float b = keyValue[rightIndex].Float;

            float result = interp == (byte)AnimationLibraryComponent.Interpolation.Linear
                ? a + (b - a) * t
                : HermiteInterpolate(a, keyOutTangent[leftIndex], b, keyInTangent[rightIndex], t);

            value = PropertyValue.FromFloat(result);
            return true;
        }

        if (kind == PropertyKind.Vec2)
        {
            Vector2 a = keyValue[leftIndex].Vec2;
            Vector2 b = keyValue[rightIndex].Vec2;
            value = PropertyValue.FromVec2(a + (b - a) * t);
            return true;
        }

        if (kind == PropertyKind.Vec3)
        {
            Vector3 a = keyValue[leftIndex].Vec3;
            Vector3 b = keyValue[rightIndex].Vec3;
            value = PropertyValue.FromVec3(a + (b - a) * t);
            return true;
        }

        if (kind == PropertyKind.Vec4)
        {
            Vector4 a = keyValue[leftIndex].Vec4;
            Vector4 b = keyValue[rightIndex].Vec4;
            value = PropertyValue.FromVec4(a + (b - a) * t);
            return true;
        }

        if (kind == PropertyKind.Color32)
        {
            Color32 a = keyValue[leftIndex].Color32;
            Color32 b = keyValue[rightIndex].Color32;
            value = PropertyValue.FromColor32(LerpColor32(a, b, t));
            return true;
        }

        value = keyValue[leftIndex];
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

    private static bool TryBlendValue(PropertyKind kind, in PropertyValue a, in PropertyValue b, float t, out PropertyValue result)
    {
        result = a;
        if (!float.IsFinite(t))
        {
            t = 0f;
        }

        t = Math.Clamp(t, 0f, 1f);

        switch (kind)
        {
            case PropertyKind.Float:
                result = PropertyValue.FromFloat(a.Float + (b.Float - a.Float) * t);
                return true;
            case PropertyKind.Vec2:
                result = PropertyValue.FromVec2(a.Vec2 + (b.Vec2 - a.Vec2) * t);
                return true;
            case PropertyKind.Vec3:
                result = PropertyValue.FromVec3(a.Vec3 + (b.Vec3 - a.Vec3) * t);
                return true;
            case PropertyKind.Vec4:
                result = PropertyValue.FromVec4(a.Vec4 + (b.Vec4 - a.Vec4) * t);
                return true;
            case PropertyKind.Color32:
                result = PropertyValue.FromColor32(LerpColor32(a.Color32, b.Color32, t));
                return true;
            default:
                result = t < 0.5f ? a : b;
                return true;
        }
    }

    private static bool TryResolvePropertySlot(AnyComponentHandle component, ushort propertyIndexHint, ulong propertyId, PropertyKind kind, out PropertySlot slot)
    {
        slot = default;

        int propertyCount = PropertyDispatcher.GetPropertyCount(component);
        if (propertyCount <= 0)
        {
            return false;
        }

        int hint = propertyIndexHint;
        if (hint >= 0 && hint < propertyCount && PropertyDispatcher.TryGetInfo(component, hint, out PropertyInfo infoHint))
        {
            if (infoHint.PropertyId == propertyId && infoHint.Kind == kind)
            {
                slot = PropertyDispatcher.GetSlot(component, hint);
                return true;
            }
        }

        for (int i = 0; i < propertyCount; i++)
        {
            if (!PropertyDispatcher.TryGetInfo(component, i, out PropertyInfo info))
            {
                continue;
            }

            if (info.PropertyId == propertyId && info.Kind == kind)
            {
                slot = PropertyDispatcher.GetSlot(component, i);
                return true;
            }
        }

        return false;
    }

    private static void WriteValue(World registry, in PropertySlot slot, PropertyKind kind, in PropertyValue value)
    {
        switch (kind)
        {
            case PropertyKind.Float:
                PropertyDispatcher.WriteFloat(registry, slot, value.Float);
                break;
            case PropertyKind.Int:
                PropertyDispatcher.WriteInt(registry, slot, value.Int);
                break;
            case PropertyKind.Bool:
            case PropertyKind.Trigger:
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

    private static void WritePrefabInstanceVariableOverrideNoUndo(UiWorkspace workspace, EntityId instanceEntity, ushort variableId, in PropertyValue value)
    {
        UiWorld world = workspace.World;
        World propertyWorld = workspace.PropertyWorld;

        if (!world.TryGetComponent(instanceEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) || !instanceAny.IsValid)
        {
            return;
        }

        var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
        var instance = PrefabInstanceComponent.Api.FromHandle(propertyWorld, instanceHandle);
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

    private static void InitializeMachine(StateMachineDefinitionComponent.ViewProxy def, StateMachineRuntimeComponent.ViewProxy runtime, ushort machineId)
    {
        Span<ushort> layerCurrentStateId = runtime.LayerCurrentStateIdSpan();
        Span<ushort> layerPreviousStateId = runtime.LayerPreviousStateIdSpan();
        Span<uint> layerStateTimeUs = runtime.LayerStateTimeUsSpan();

        ushort layerCount = def.LayerCount;
        if (layerCount > StateMachineDefinitionComponent.MaxLayers)
        {
            layerCount = StateMachineDefinitionComponent.MaxLayers;
        }

        for (int layerSlot = 0; layerSlot < layerCount; layerSlot++)
        {
            if (def.LayerMachineId[layerSlot] != machineId)
            {
                continue;
            }

            ushort entryTarget = def.LayerEntryTargetStateId[layerSlot];
            layerCurrentStateId[layerSlot] = entryTarget;
            layerPreviousStateId[layerSlot] = 0;
            layerStateTimeUs[layerSlot] = 0;
        }
    }

    private static bool TrySelectTransition(
        StateMachineDefinitionComponent.ViewProxy def,
        StateMachineRuntimeComponent.ViewProxy runtime,
        ushort layerId,
        int layerSlot,
        ushort currentStateId,
        bool hasInstance,
        PrefabInstanceComponent.ViewProxy instance,
        bool hasSourcePrefabVars,
        PrefabVariablesComponent.ViewProxy sourcePrefabVars,
        out ushort transitionId,
        out ushort nextStateId,
        out int transitionSlot)
    {
        transitionId = 0;
        nextStateId = currentStateId;
        transitionSlot = -1;

        ushort transitionCount = def.TransitionCount;
        if (transitionCount > StateMachineDefinitionComponent.MaxTransitions)
        {
            transitionCount = StateMachineDefinitionComponent.MaxTransitions;
        }

        // Prefer direct transitions from current state; then AnyState; then Entry (when not yet in a state).
        if (TrySelectTransitionPass(def, layerId, currentStateId, matchAnyState: false, matchEntry: false, hasInstance, instance, hasSourcePrefabVars, sourcePrefabVars, transitionCount, out transitionId, out nextStateId, out transitionSlot))
        {
            return true;
        }

        if (TrySelectTransitionPass(def, layerId, currentStateId, matchAnyState: true, matchEntry: false, hasInstance, instance, hasSourcePrefabVars, sourcePrefabVars, transitionCount, out transitionId, out nextStateId, out transitionSlot))
        {
            return true;
        }

        if (currentStateId == 0 &&
            TrySelectTransitionPass(def, layerId, currentStateId, matchAnyState: false, matchEntry: true, hasInstance, instance, hasSourcePrefabVars, sourcePrefabVars, transitionCount, out transitionId, out nextStateId, out transitionSlot))
        {
            return true;
        }

        // If we're still at entry and there are no explicit entry transitions, use the entry target.
        if (currentStateId == 0)
        {
            ushort entryTarget = def.LayerEntryTargetStateId[layerSlot];
            if (entryTarget != 0)
            {
                transitionId = 0;
                nextStateId = entryTarget;
                transitionSlot = -1;
                return true;
            }
        }

        return false;
    }

    private static bool TrySelectTransitionPass(
        StateMachineDefinitionComponent.ViewProxy def,
        ushort layerId,
        ushort currentStateId,
        bool matchAnyState,
        bool matchEntry,
        bool hasInstance,
        PrefabInstanceComponent.ViewProxy instance,
        bool hasSourcePrefabVars,
        PrefabVariablesComponent.ViewProxy sourcePrefabVars,
        ushort transitionCount,
        out ushort transitionId,
        out ushort nextStateId,
        out int transitionSlot)
    {
        transitionId = 0;
        nextStateId = currentStateId;
        transitionSlot = -1;

        for (int i = 0; i < transitionCount; i++)
        {
            if (def.TransitionLayerId[i] != layerId)
            {
                continue;
            }

            var fromKind = (StateMachineDefinitionComponent.TransitionFromKind)def.TransitionFromKindValue[i];
            ushort fromStateId = def.TransitionFromStateId[i];

            if (matchAnyState)
            {
                if (fromKind != StateMachineDefinitionComponent.TransitionFromKind.AnyState)
                {
                    continue;
                }
            }
            else if (matchEntry)
            {
                if (fromKind != StateMachineDefinitionComponent.TransitionFromKind.Entry)
                {
                    continue;
                }
            }
            else
            {
                if (fromKind != StateMachineDefinitionComponent.TransitionFromKind.State || fromStateId != currentStateId)
                {
                    continue;
                }
            }

            if (!EvaluateTransitionConditions(def, i, hasInstance, instance, hasSourcePrefabVars, sourcePrefabVars))
            {
                continue;
            }

            var toKind = (StateMachineDefinitionComponent.TransitionToKind)def.TransitionToKindValue[i];
            ushort toStateId = def.TransitionToStateId[i];

            transitionId = def.TransitionId[i];
            nextStateId = toKind == StateMachineDefinitionComponent.TransitionToKind.Exit ? (ushort)0 : toStateId;
            transitionSlot = i;
            return true;
        }

        return false;
    }

    private static bool EvaluateTransitionConditions(
        StateMachineDefinitionComponent.ViewProxy def,
        int transitionSlot,
        bool hasInstance,
        PrefabInstanceComponent.ViewProxy instance,
        bool hasSourcePrefabVars,
        PrefabVariablesComponent.ViewProxy sourcePrefabVars)
    {
        ushort conditionCountTotal = def.ConditionCount;
        if (conditionCountTotal > StateMachineDefinitionComponent.MaxConditions)
        {
            conditionCountTotal = StateMachineDefinitionComponent.MaxConditions;
        }

        ushort start = def.TransitionConditionStart[transitionSlot];
        byte count = def.TransitionConditionCount[transitionSlot];
        if (count == 0)
        {
            return true;
        }

        if (start >= conditionCountTotal)
        {
            return false;
        }

        uint end = (uint)start + count;
        if (end > conditionCountTotal)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            int conditionSlot = start + i;
            ushort variableId = def.ConditionVariableId[conditionSlot];
            var op = (StateMachineDefinitionComponent.ConditionOp)def.ConditionOpValue[conditionSlot];
            PropertyValue compareValue = def.ConditionCompareValue[conditionSlot];

            if (!TryGetVariableValueAndKind(hasInstance, instance, hasSourcePrefabVars, sourcePrefabVars, variableId, out PropertyKind kind, out PropertyValue current))
            {
                return false;
            }

            if (!EvaluateCondition(kind, current, op, compareValue))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetVariableValueAndKind(
        bool hasInstance,
        PrefabInstanceComponent.ViewProxy instance,
        bool hasSourcePrefabVars,
        PrefabVariablesComponent.ViewProxy sourcePrefabVars,
        ushort variableId,
        out PropertyKind kind,
        out PropertyValue value)
    {
        kind = default;
        value = default;

        if (!hasSourcePrefabVars || sourcePrefabVars.VariableCount == 0)
        {
            return false;
        }

        ushort count = sourcePrefabVars.VariableCount;
        if (count > PrefabVariablesComponent.MaxVariables)
        {
            count = PrefabVariablesComponent.MaxVariables;
        }

        ReadOnlySpan<ushort> prefabVarId = sourcePrefabVars.VariableIdReadOnlySpan();
        ReadOnlySpan<int> prefabKind = sourcePrefabVars.KindReadOnlySpan();
        ReadOnlySpan<PropertyValue> prefabDefaults = sourcePrefabVars.DefaultValueReadOnlySpan();

        int prefabIndex = -1;
        for (int i = 0; i < count; i++)
        {
            if (prefabVarId[i] == variableId)
            {
                prefabIndex = i;
                break;
            }
        }

        if (prefabIndex < 0)
        {
            return false;
        }

        kind = (PropertyKind)prefabKind[prefabIndex];
        value = prefabDefaults[prefabIndex];

        if (!hasInstance || instance.ValueCount == 0)
        {
            return true;
        }

        ReadOnlySpan<ushort> instanceVarId = instance.VariableIdReadOnlySpan();
        ReadOnlySpan<PropertyValue> instanceValues = instance.ValueReadOnlySpan();
        ushort instanceCount = instance.ValueCount;
        if (instanceCount > PrefabInstanceComponent.MaxVariables)
        {
            instanceCount = PrefabInstanceComponent.MaxVariables;
        }

        int instanceIndex = -1;
        for (int i = 0; i < instanceCount; i++)
        {
            if (instanceVarId[i] == variableId)
            {
                instanceIndex = i;
                break;
            }
        }

        if (instanceIndex < 0)
        {
            return true;
        }

        bool isOverridden = (instance.OverrideMask & (1UL << instanceIndex)) != 0;
        if (!isOverridden)
        {
            return true;
        }

        value = instanceValues[instanceIndex];
        return true;
    }

    private static bool EvaluateCondition(PropertyKind kind, PropertyValue current, StateMachineDefinitionComponent.ConditionOp op, PropertyValue compareValue)
    {
        switch (op)
        {
            case StateMachineDefinitionComponent.ConditionOp.IsTrue:
                return (kind == PropertyKind.Bool || kind == PropertyKind.Trigger) && current.Bool;
            case StateMachineDefinitionComponent.ConditionOp.IsFalse:
                return (kind == PropertyKind.Bool || kind == PropertyKind.Trigger) && !current.Bool;
            case StateMachineDefinitionComponent.ConditionOp.Equals:
                return current.Equals(kind, compareValue);
            case StateMachineDefinitionComponent.ConditionOp.NotEquals:
                return !current.Equals(kind, compareValue);
            case StateMachineDefinitionComponent.ConditionOp.GreaterThan:
            case StateMachineDefinitionComponent.ConditionOp.LessThan:
            case StateMachineDefinitionComponent.ConditionOp.GreaterOrEqual:
            case StateMachineDefinitionComponent.ConditionOp.LessOrEqual:
                return Compare(kind, current, op, compareValue);
            default:
                return false;
        }
    }

    private static bool Compare(PropertyKind kind, PropertyValue current, StateMachineDefinitionComponent.ConditionOp op, PropertyValue compareValue)
    {
        switch (kind)
        {
            case PropertyKind.Int:
                return CompareInt(current.Int, op, compareValue.Int);
            case PropertyKind.Float:
                return CompareFloat(current.Float, op, compareValue.Float);
            case PropertyKind.Fixed64:
                return CompareFixed64(current.Fixed64, op, compareValue.Fixed64);
            default:
                return false;
        }
    }

    private static bool CompareInt(int a, StateMachineDefinitionComponent.ConditionOp op, int b)
    {
        return op switch
        {
            StateMachineDefinitionComponent.ConditionOp.GreaterThan => a > b,
            StateMachineDefinitionComponent.ConditionOp.LessThan => a < b,
            StateMachineDefinitionComponent.ConditionOp.GreaterOrEqual => a >= b,
            StateMachineDefinitionComponent.ConditionOp.LessOrEqual => a <= b,
            _ => false
        };
    }

    private static bool CompareFloat(float a, StateMachineDefinitionComponent.ConditionOp op, float b)
    {
        return op switch
        {
            StateMachineDefinitionComponent.ConditionOp.GreaterThan => a > b,
            StateMachineDefinitionComponent.ConditionOp.LessThan => a < b,
            StateMachineDefinitionComponent.ConditionOp.GreaterOrEqual => a >= b,
            StateMachineDefinitionComponent.ConditionOp.LessOrEqual => a <= b,
            _ => false
        };
    }

    private static bool CompareFixed64(FixedMath.Fixed64 a, StateMachineDefinitionComponent.ConditionOp op, FixedMath.Fixed64 b)
    {
        return op switch
        {
            StateMachineDefinitionComponent.ConditionOp.GreaterThan => a > b,
            StateMachineDefinitionComponent.ConditionOp.LessThan => a < b,
            StateMachineDefinitionComponent.ConditionOp.GreaterOrEqual => a >= b,
            StateMachineDefinitionComponent.ConditionOp.LessOrEqual => a <= b,
            _ => false
        };
    }
}
