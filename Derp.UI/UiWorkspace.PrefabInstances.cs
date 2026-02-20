using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using Core;
using Pooled.Runtime;
using Property;
using Property.Runtime;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    internal uint _prefabInstanceSourcePrefabStableId;
    private int _prefabInstanceSourceOptionsPrefabCount;
    private string[] _prefabInstanceSourceOptions = Array.Empty<string>();
    private uint[] _prefabInstanceSourceStableIds = Array.Empty<uint>();

    internal bool TryGetPrefabInstanceSourceDropdown(out string[] options, out int selectedIndex)
    {
        options = Array.Empty<string>();
        selectedIndex = -1;

        if (_prefabs.Count == 0)
        {
            return false;
        }

        EnsurePrefabInstanceSourceOptions();

        options = _prefabInstanceSourceOptions;
        selectedIndex = GetPrefabInstanceSourceSelectedIndex();
        if (selectedIndex < 0 && _prefabInstanceSourceStableIds.Length > 0)
        {
            selectedIndex = 0;
        }

        return options.Length > 0;
    }

    internal bool TrySetPrefabInstanceSourceByIndex(int index)
    {
        EnsurePrefabInstanceSourceOptions();
        if ((uint)index >= (uint)_prefabInstanceSourceStableIds.Length)
        {
            return false;
        }

        _prefabInstanceSourcePrefabStableId = _prefabInstanceSourceStableIds[index];
        return _prefabInstanceSourcePrefabStableId != 0;
    }

    internal bool TryGetPrefabDropdownOptions(out string[] options, out uint[] stableIds)
    {
        options = Array.Empty<string>();
        stableIds = Array.Empty<uint>();

        if (_prefabs.Count == 0)
        {
            return false;
        }

        EnsurePrefabInstanceSourceOptions();
        options = _prefabInstanceSourceOptions;
        stableIds = _prefabInstanceSourceStableIds;
        return options.Length > 0 && stableIds.Length == options.Length;
    }

    private void EnsurePrefabInstanceSourceOptions()
    {
        int prefabCount = _prefabs.Count;
        if (_prefabInstanceSourceOptionsPrefabCount == prefabCount &&
            _prefabInstanceSourceOptions.Length == prefabCount &&
            _prefabInstanceSourceStableIds.Length == prefabCount)
        {
            return;
        }

        _prefabInstanceSourceOptionsPrefabCount = prefabCount;
        _prefabInstanceSourceOptions = new string[prefabCount];
        _prefabInstanceSourceStableIds = new uint[prefabCount];

        for (int prefabIndex = 0; prefabIndex < prefabCount; prefabIndex++)
        {
            int prefabId = _prefabs[prefabIndex].Id;
            EntityId prefabEntity = EntityId.Null;
            if (prefabId > 0)
            {
                TryGetEntityById(_prefabEntityById, prefabId, out prefabEntity);
            }

            uint stableId = prefabEntity.IsNull ? 0u : _world.GetStableId(prefabEntity);
            _prefabInstanceSourceStableIds[prefabIndex] = stableId;

            string label = $"Prefab {prefabId}";
            if (stableId != 0 && TryGetLayerName(stableId, out string name) && !string.IsNullOrEmpty(name))
            {
                label = name;
            }

            _prefabInstanceSourceOptions[prefabIndex] = label;
        }

        if (_prefabInstanceSourcePrefabStableId == 0 && _prefabInstanceSourceStableIds.Length > 0)
        {
            uint activePrefabStableId = _selectedPrefabEntity.IsNull ? 0u : _world.GetStableId(_selectedPrefabEntity);
            for (int i = 0; i < _prefabInstanceSourceStableIds.Length; i++)
            {
                uint candidate = _prefabInstanceSourceStableIds[i];
                if (candidate != 0 && candidate != activePrefabStableId)
                {
                    _prefabInstanceSourcePrefabStableId = candidate;
                    break;
                }
            }

            if (_prefabInstanceSourcePrefabStableId == 0)
            {
                _prefabInstanceSourcePrefabStableId = _prefabInstanceSourceStableIds[0];
            }
        }
    }

    private int GetPrefabInstanceSourceSelectedIndex()
    {
        if (_prefabInstanceSourcePrefabStableId == 0)
        {
            return -1;
        }

        for (int i = 0; i < _prefabInstanceSourceStableIds.Length; i++)
        {
            if (_prefabInstanceSourceStableIds[i] == _prefabInstanceSourcePrefabStableId)
            {
                return i;
            }
        }

        return -1;
    }

    private void UpdatePrefabInstances()
    {
        int entityCount = _world.EntityCount;
        for (int entityIndex = 1; entityIndex <= entityCount; entityIndex++)
        {
            var instanceEntity = new EntityId(entityIndex);
            if (_world.GetNodeType(instanceEntity) != UiNodeType.PrefabInstance)
            {
                continue;
            }

            if (!_world.TryGetComponent(instanceEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny))
            {
                continue;
            }

            var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var instance = PrefabInstanceComponent.Api.FromHandle(_propertyWorld, instanceHandle);
            if (!instance.IsAlive)
            {
                continue;
            }

            uint sourcePrefabStableId = instance.SourcePrefabStableId;
            if (sourcePrefabStableId == 0)
            {
                continue;
            }

            EntityId sourcePrefabEntity = _world.GetEntityByStableId(sourcePrefabStableId);
            if (sourcePrefabEntity.IsNull || _world.GetNodeType(sourcePrefabEntity) != UiNodeType.Prefab)
            {
                continue;
            }

            EnsurePrefabInstanceCanvasComponent(instanceEntity, sourcePrefabEntity);
            SyncPrefabInstanceLayoutChildFromSource(instanceEntity, sourcePrefabEntity, sourcePrefabStableId);

            uint bindingsRevision = 0;
            if (_world.TryGetComponent(sourcePrefabEntity, PrefabBindingsComponent.Api.PoolIdConst, out AnyComponentHandle bindingsAny))
            {
                var bindingsHandle = new PrefabBindingsComponentHandle(bindingsAny.Index, bindingsAny.Generation);
                var bindings = PrefabBindingsComponent.Api.FromHandle(_propertyWorld, bindingsHandle);
                if (bindings.IsAlive)
                {
                    bindingsRevision = bindings.Revision;
                }
            }

            PrefabVariablesComponent.ViewProxy prefabVars = default;
            if (_world.TryGetComponent(sourcePrefabEntity, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle prefabVarsAny) &&
                prefabVarsAny.IsValid)
            {
                var prefabVarsHandle = new PrefabVariablesComponentHandle(prefabVarsAny.Index, prefabVarsAny.Generation);
                prefabVars = PrefabVariablesComponent.Api.FromHandle(_propertyWorld, prefabVarsHandle);
                if (prefabVars.IsAlive)
                {
                    if (NeedsPrefabInstanceVariableSync(instance, prefabVars))
                    {
                        SyncPrefabInstanceVariables(instance, prefabVars);
                    }

                    TryInferPrefabInstanceOverrideMaskFromDefaults(instance, prefabVars);
                }
                else
                {
                    prefabVars = default;
                }
            }

            uint sourceRevision = GetPrefabRevision(sourcePrefabEntity);
            if (instance.SourcePrefabRevisionAtBuild == sourceRevision)
            {
                // Migration + steady-state: ensure expanded-root proxy exists even when no rebuild is required.
                if (!TryFindExpandedRootProxy(instanceEntity, sourcePrefabStableId, out EntityId expandedRootProxy))
                {
                    RebuildPrefabInstanceExpansion(instanceEntity, sourcePrefabEntity, sourceRevision);
                    RebuildPrefabInstanceBindingCache(instanceEntity, instance, sourcePrefabStableId, bindingsRevision);
                }
                else
                {
                    SyncExpandedRootProxy(instanceEntity, sourcePrefabEntity, expandedRootProxy);
                }

                if (instance.SourcePrefabBindingsRevisionAtBuild != bindingsRevision)
                {
                    RebuildPrefabInstanceBindingCache(instanceEntity, instance, sourcePrefabStableId, bindingsRevision);
                }

                ApplyPrefabInstanceBindings(instanceEntity, instance, prefabVars);
                ProcessListLayoutBindings(instanceEntity, instance, prefabVars);
                continue;
            }

            if (WouldPrefabInstanceCreateCycle(instanceEntity, sourcePrefabStableId))
            {
                DestroyAllChildren(instanceEntity);
                instance.SourcePrefabRevisionAtBuild = sourceRevision;
                instance.SourcePrefabBindingsRevisionAtBuild = 0;
                continue;
            }

            RebuildPrefabInstanceExpansion(instanceEntity, sourcePrefabEntity, sourceRevision);

            RebuildPrefabInstanceBindingCache(instanceEntity, instance, sourcePrefabStableId, bindingsRevision);
            ApplyPrefabInstanceBindings(instanceEntity, instance, prefabVars);
            ProcessListLayoutBindings(instanceEntity, instance, prefabVars);
        }
    }

    private void SyncPrefabInstanceLayoutChildFromSource(EntityId instanceEntity, EntityId sourcePrefabEntity, uint sourcePrefabStableId)
    {
        if (instanceEntity.IsNull || sourcePrefabEntity.IsNull || sourcePrefabStableId == 0)
        {
            return;
        }

        if (!_world.TryGetComponent(instanceEntity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle instanceTransformAny) || !instanceTransformAny.IsValid)
        {
            return;
        }

        if (!_world.TryGetComponent(sourcePrefabEntity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle sourceTransformAny) || !sourceTransformAny.IsValid)
        {
            return;
        }

        var instanceTransformHandle = new TransformComponentHandle(instanceTransformAny.Index, instanceTransformAny.Generation);
        var instanceTransform = TransformComponent.Api.FromHandle(_propertyWorld, instanceTransformHandle);
        if (!instanceTransform.IsAlive)
        {
            return;
        }

        var sourceTransformHandle = new TransformComponentHandle(sourceTransformAny.Index, sourceTransformAny.Generation);
        var sourceTransform = TransformComponent.Api.FromHandle(_propertyWorld, sourceTransformHandle);
        if (!sourceTransform.IsAlive)
        {
            return;
        }

        // Inherit layout-child properties from the source prefab root by default (Unity-style),
        // then apply any per-instance overrides stored on the instance root.
        instanceTransform.LayoutChildIgnoreLayout = sourceTransform.LayoutChildIgnoreLayout;
        instanceTransform.LayoutChildMargin = sourceTransform.LayoutChildMargin;
        instanceTransform.LayoutChildAlignSelf = sourceTransform.LayoutChildAlignSelf;
        instanceTransform.LayoutChildFlexGrow = sourceTransform.LayoutChildFlexGrow;
        instanceTransform.LayoutChildFlexShrink = sourceTransform.LayoutChildFlexShrink;
        instanceTransform.LayoutChildPreferredSize = sourceTransform.LayoutChildPreferredSize;

        if (!_world.TryGetComponent(instanceEntity, PrefabInstancePropertyOverridesComponent.Api.PoolIdConst, out AnyComponentHandle overridesAny) || !overridesAny.IsValid)
        {
            return;
        }

        var overridesHandle = new PrefabInstancePropertyOverridesComponentHandle(overridesAny.Index, overridesAny.Generation);
        var overrides = PrefabInstancePropertyOverridesComponent.Api.FromHandle(_propertyWorld, overridesHandle);
        if (!overrides.IsAlive || overrides.RootOverrideCount == 0)
        {
            return;
        }

        int rootCount = overrides.RootOverrideCount;
        if (rootCount > PrefabInstancePropertyOverridesComponent.MaxOverrides)
        {
            rootCount = PrefabInstancePropertyOverridesComponent.MaxOverrides;
        }

        StringHandle layoutChildGroup = "Layout Child";

        ReadOnlySpan<uint> srcNode = overrides.SourceNodeStableIdReadOnlySpan();
        ReadOnlySpan<ushort> componentKind = overrides.ComponentKindReadOnlySpan();
        ReadOnlySpan<ushort> propertyIndexHint = overrides.PropertyIndexHintReadOnlySpan();
        ReadOnlySpan<ulong> propertyId = overrides.PropertyIdReadOnlySpan();
        ReadOnlySpan<ushort> propertyKind = overrides.PropertyKindReadOnlySpan();
        ReadOnlySpan<PropertyValue> value = overrides.ValueReadOnlySpan();

        for (int i = 0; i < rootCount; i++)
        {
            if (srcNode[i] != sourcePrefabStableId)
            {
                continue;
            }

            if (componentKind[i] != TransformComponent.Api.PoolIdConst)
            {
                continue;
            }

            PropertyKind pk = (PropertyKind)propertyKind[i];
            if (!TryResolveBoundSlot(instanceTransformAny, propertyIndexHint[i], propertyId[i], pk, out PropertySlot slot))
            {
                continue;
            }

            if (!PropertyDispatcher.TryGetInfo(instanceTransformAny, slot.PropertyIndex, out PropertyInfo info))
            {
                continue;
            }

            if (info.Group != layoutChildGroup)
            {
                continue;
            }

            WriteBoundValue(slot, pk, value[i]);
        }
    }

    private void ApplySelectedPrefabDefinitionBindings()
    {
        EntityId prefabEntity = _selectedPrefabEntity;
        if (prefabEntity.IsNull || _world.GetNodeType(prefabEntity) != UiNodeType.Prefab)
        {
            return;
        }

        if (!_world.TryGetComponent(prefabEntity, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) || !varsAny.IsValid)
        {
            return;
        }

        if (!_world.TryGetComponent(prefabEntity, PrefabBindingsComponent.Api.PoolIdConst, out AnyComponentHandle bindingsAny) || !bindingsAny.IsValid)
        {
            return;
        }

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        var vars = PrefabVariablesComponent.Api.FromHandle(_propertyWorld, varsHandle);
        if (!vars.IsAlive || vars.VariableCount == 0)
        {
            return;
        }

        var bindingsHandle = new PrefabBindingsComponentHandle(bindingsAny.Index, bindingsAny.Generation);
        var bindings = PrefabBindingsComponent.Api.FromHandle(_propertyWorld, bindingsHandle);
        if (!bindings.IsAlive || bindings.BindingCount == 0)
        {
            return;
        }

        ushort bindingCount = bindings.BindingCount;
        if (bindingCount > PrefabBindingsComponent.MaxBindings)
        {
            bindingCount = PrefabBindingsComponent.MaxBindings;
        }

        ushort varCount = vars.VariableCount;
        if (varCount > PrefabVariablesComponent.MaxVariables)
        {
            varCount = PrefabVariablesComponent.MaxVariables;
        }

        ReadOnlySpan<ushort> varsId = vars.VariableIdReadOnlySpan();
        Span<PropertyValue> varsDefault = vars.DefaultValueSpan();
        Span<int> varsKind = vars.KindSpan();

        ReadOnlySpan<ushort> bindVarId = bindings.VariableIdReadOnlySpan();
        ReadOnlySpan<byte> bindDirection = bindings.DirectionReadOnlySpan();
        ReadOnlySpan<uint> bindTargetSourceNode = bindings.TargetSourceNodeStableIdReadOnlySpan();
        ReadOnlySpan<ushort> bindTargetComponentKind = bindings.TargetComponentKindReadOnlySpan();
        ReadOnlySpan<ushort> bindPropertyIndex = bindings.PropertyIndexHintReadOnlySpan();
        ReadOnlySpan<ulong> bindPropertyId = bindings.PropertyIdReadOnlySpan();
        ReadOnlySpan<ushort> bindPropertyKind = bindings.PropertyKindReadOnlySpan();

        // Pass 1: Property -> Variable (bind-to-source).
        for (int i = 0; i < bindingCount; i++)
        {
            if (bindDirection[i] != 1)
            {
                continue;
            }

            ushort variableId = bindVarId[i];
            if (variableId == 0)
            {
                continue;
            }

            int varIndex = -1;
            for (int v = 0; v < varCount; v++)
            {
                if (varsId[v] == variableId)
                {
                    varIndex = v;
                    break;
                }
            }
            if (varIndex < 0)
            {
                continue;
            }

            uint targetStableId = bindTargetSourceNode[i];
            if (targetStableId == 0)
            {
                continue;
            }

            EntityId target = _world.GetEntityByStableId(targetStableId);
            if (target.IsNull)
            {
                continue;
            }
            if (!IsDescendantOrSelf(_world, target, prefabEntity))
            {
                continue;
            }

            ushort componentKind = bindTargetComponentKind[i];
            if (componentKind == 0)
            {
                continue;
            }

            if (!_world.TryGetComponent(target, componentKind, out AnyComponentHandle componentAny) || !componentAny.IsValid)
            {
                continue;
            }

            PropertyKind kind = (PropertyKind)bindPropertyKind[i];
            if (!TryResolveBoundSlot(componentAny, bindPropertyIndex[i], bindPropertyId[i], kind, out PropertySlot slot))
            {
                continue;
            }

            PropertyValue value = ReadBoundValue(slot, kind);
            varsDefault[varIndex] = value;
            varsKind[varIndex] = (int)kind;
        }

        for (int i = 0; i < bindingCount; i++)
        {
            ushort variableId = bindVarId[i];
            if (variableId == 0)
            {
                continue;
            }
            if (bindDirection[i] != 0)
            {
                continue;
            }

            PropertyValue value = default;
            bool foundVar = false;
            for (int v = 0; v < varCount; v++)
            {
                if (varsId[v] == variableId)
                {
                    value = varsDefault[v];
                    foundVar = true;
                    break;
                }
            }
            if (!foundVar)
            {
                continue;
            }

            uint targetStableId = bindTargetSourceNode[i];
            if (targetStableId == 0)
            {
                continue;
            }

            EntityId target = _world.GetEntityByStableId(targetStableId);
            if (target.IsNull)
            {
                continue;
            }
            if (!IsDescendantOrSelf(_world, target, prefabEntity))
            {
                continue;
            }

            ushort componentKind = bindTargetComponentKind[i];
            if (componentKind == 0)
            {
                continue;
            }

            if (!_world.TryGetComponent(target, componentKind, out AnyComponentHandle componentAny) || !componentAny.IsValid)
            {
                continue;
            }

            PropertyKind kind = (PropertyKind)bindPropertyKind[i];
            if (!TryResolveBoundSlot(componentAny, bindPropertyIndex[i], bindPropertyId[i], kind, out PropertySlot slot))
            {
                continue;
            }

            WriteBoundValue(slot, kind, value);
        }

        ProcessSelectedPrefabDefinitionListLayoutBindings(prefabEntity, vars, bindings);
    }

    private EntityId[] _listBindTraversalScratch = new EntityId[256];
    private EntityId[] _listBindDestroyScratch = new EntityId[256];

    private void EnsureListBindScratchCapacity(int required)
    {
        if (_listBindTraversalScratch.Length < required)
        {
            _listBindTraversalScratch = new EntityId[Math.Max(required, _listBindTraversalScratch.Length * 2)];
        }
        if (_listBindDestroyScratch.Length < required)
        {
            _listBindDestroyScratch = new EntityId[Math.Max(required, _listBindDestroyScratch.Length * 2)];
        }
    }

    private void ProcessSelectedPrefabDefinitionListLayoutBindings(
        EntityId prefabEntity,
        PrefabVariablesComponent.ViewProxy vars,
        PrefabBindingsComponent.ViewProxy bindings)
    {
        if (prefabEntity.IsNull || !vars.IsAlive || !bindings.IsAlive)
        {
            return;
        }

        const byte DirectionListBind = 2;

        uint prefabStableId = _world.GetStableId(prefabEntity);

        PrefabListDataComponent.ViewProxy listData = default;
        if (_world.TryGetComponent(prefabEntity, PrefabListDataComponent.Api.PoolIdConst, out AnyComponentHandle listAny) && listAny.IsValid)
        {
            var handle = new PrefabListDataComponentHandle(listAny.Index, listAny.Generation);
            listData = PrefabListDataComponent.Api.FromHandle(_propertyWorld, handle);
        }

        ushort bindingCount = bindings.BindingCount;
        if (bindingCount > PrefabBindingsComponent.MaxBindings)
        {
            bindingCount = PrefabBindingsComponent.MaxBindings;
        }

        ReadOnlySpan<ushort> bindVarId = bindings.VariableIdReadOnlySpan();
        ReadOnlySpan<byte> bindDirection = bindings.DirectionReadOnlySpan();
        ReadOnlySpan<uint> bindTargetSourceNode = bindings.TargetSourceNodeStableIdReadOnlySpan();

        ushort varCount = vars.VariableCount;
        if (varCount > PrefabVariablesComponent.MaxVariables)
        {
            varCount = PrefabVariablesComponent.MaxVariables;
        }

        ReadOnlySpan<ushort> varsId = vars.VariableIdReadOnlySpan();
        ReadOnlySpan<int> varsKind = vars.KindReadOnlySpan();
        ReadOnlySpan<PropertyValue> varsDefault = vars.DefaultValueReadOnlySpan();

        Span<uint> activeLayoutStableId = stackalloc uint[PrefabBindingsComponent.MaxBindings];
        Span<ushort> activeListVariableId = stackalloc ushort[PrefabBindingsComponent.MaxBindings];
        int activeCount = 0;

        for (int bindIndex = 0; bindIndex < bindingCount; bindIndex++)
        {
            if (bindDirection[bindIndex] != DirectionListBind)
            {
                continue;
            }

            ushort listVariableId = bindVarId[bindIndex];
            uint layoutStableId = bindTargetSourceNode[bindIndex];
            if (listVariableId == 0 || layoutStableId == 0)
            {
                continue;
            }

            if (activeCount < activeLayoutStableId.Length)
            {
                activeLayoutStableId[activeCount] = layoutStableId;
                activeListVariableId[activeCount] = listVariableId;
                activeCount++;
            }

            EntityId layoutEntity = _world.GetEntityByStableId(layoutStableId);
            if (layoutEntity.IsNull || !IsDescendantOrSelf(_world, layoutEntity, prefabEntity))
            {
                continue;
            }

            int listVarIndex = -1;
            for (int v = 0; v < varCount; v++)
            {
                if (varsId[v] == listVariableId)
                {
                    listVarIndex = v;
                    break;
                }
            }
            if (listVarIndex < 0 || (PropertyKind)varsKind[listVarIndex] != PropertyKind.List)
            {
                DestroyAllListGeneratedChildren(layoutEntity, layoutStableId, listVariableId);
                continue;
            }

            uint typePrefabStableId = varsDefault[listVarIndex].UInt;
            if (typePrefabStableId == 0 || (prefabStableId != 0 && typePrefabStableId == prefabStableId))
            {
                DestroyAllListGeneratedChildren(layoutEntity, layoutStableId, listVariableId);
                continue;
            }

            EntityId typePrefabEntity = _world.GetEntityByStableId(typePrefabStableId);
            if (typePrefabEntity.IsNull || _world.GetNodeType(typePrefabEntity) != UiNodeType.Prefab)
            {
                DestroyAllListGeneratedChildren(layoutEntity, layoutStableId, listVariableId);
                continue;
            }

            PrefabVariablesComponent.ViewProxy typeVars = default;
            if (_world.TryGetComponent(typePrefabEntity, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle typeVarsAny) && typeVarsAny.IsValid)
            {
                var typeVarsHandle = new PrefabVariablesComponentHandle(typeVarsAny.Index, typeVarsAny.Generation);
                typeVars = PrefabVariablesComponent.Api.FromHandle(_propertyWorld, typeVarsHandle);
            }

            int itemCount = 0;
            int itemStart = 0;
            int stride = 0;
            ReadOnlySpan<PropertyValue> flatItems = default;
            if (listData.IsAlive && TryFindListEntryIndex(listData, listVariableId, out int entryIndex))
            {
                ReadOnlySpan<ushort> entryItemCount = listData.EntryItemCountReadOnlySpan();
                ReadOnlySpan<ushort> entryItemStart = listData.EntryItemStartReadOnlySpan();
                ReadOnlySpan<ushort> entryFieldCount = listData.EntryFieldCountReadOnlySpan();
                flatItems = listData.ItemsReadOnlySpan();
                itemCount = entryItemCount[entryIndex];
                itemStart = entryItemStart[entryIndex];
                stride = entryFieldCount[entryIndex];
            }

            SyncListGeneratedChildren(
                layoutEntity,
                layoutStableId,
                listVariableId,
                typePrefabStableId,
                typeVars,
                itemCount,
                itemStart,
                stride,
                flatItems);
        }

        CleanupStaleListGeneratedChildrenInPrefab(prefabEntity, activeLayoutStableId, activeListVariableId, activeCount);
    }

    private void CleanupStaleListGeneratedChildrenInPrefab(EntityId prefabEntity, ReadOnlySpan<uint> activeLayoutStableId, ReadOnlySpan<ushort> activeListVariableId, int activeCount)
    {
        if (prefabEntity.IsNull)
        {
            return;
        }

        ReadOnlySpan<EntityId> rootChildren = _world.GetChildren(prefabEntity);
        if (rootChildren.IsEmpty)
        {
            return;
        }

        EnsureListBindScratchCapacity(rootChildren.Length + 16);

        int stackCount = 0;
        for (int i = 0; i < rootChildren.Length; i++)
        {
            _listBindTraversalScratch[stackCount++] = rootChildren[i];
        }

        int destroyCount = 0;

        while (stackCount > 0)
        {
            EntityId current = _listBindTraversalScratch[--stackCount];

            if (_world.TryGetComponent(current, ListGeneratedComponent.Api.PoolIdConst, out AnyComponentHandle tagAny) && tagAny.IsValid)
            {
                var tagHandle = new ListGeneratedComponentHandle(tagAny.Index, tagAny.Generation);
                var tag = ListGeneratedComponent.Api.FromHandle(_propertyWorld, tagHandle);
                if (tag.IsAlive)
                {
                    EntityId layoutEntity = _world.GetEntityByStableId(tag.SourceLayoutStableId);
                    if (layoutEntity.IsNull ||
                        !_world.HasComponent(layoutEntity, PrefabExpandedComponent.Api.PoolIdConst))
                    {
                    bool isActive = false;
                    for (int i = 0; i < activeCount; i++)
                    {
                        if (activeLayoutStableId[i] == tag.SourceLayoutStableId && activeListVariableId[i] == tag.ListVariableId)
                        {
                            isActive = true;
                            break;
                        }
                    }

                    if (!isActive)
                    {
                        if (destroyCount >= _listBindDestroyScratch.Length)
                        {
                            EnsureListBindScratchCapacity(_listBindDestroyScratch.Length * 2);
                        }
                        _listBindDestroyScratch[destroyCount++] = current;
                    }
                    }
                }
            }

            ReadOnlySpan<EntityId> children = _world.GetChildren(current);
            if (stackCount + children.Length > _listBindTraversalScratch.Length)
            {
                EnsureListBindScratchCapacity(Math.Max(stackCount + children.Length, _listBindTraversalScratch.Length * 2));
            }
            for (int i = 0; i < children.Length; i++)
            {
                _listBindTraversalScratch[stackCount++] = children[i];
            }
        }

        for (int i = 0; i < destroyCount; i++)
        {
            _world.DestroyEntity(_listBindDestroyScratch[i]);
        }
    }

    private void RebuildPrefabInstanceBindingCache(EntityId instanceEntity, PrefabInstanceComponent.ViewProxy instance, uint sourcePrefabStableId, uint bindingsRevision)
    {
        if (instanceEntity.IsNull)
        {
            return;
        }

        PrefabInstanceBindingCacheComponent.ViewProxy cache = default;
        if (_world.TryGetComponent(instanceEntity, PrefabInstanceBindingCacheComponent.Api.PoolIdConst, out AnyComponentHandle cacheAny) && cacheAny.IsValid)
        {
            var cacheHandle = new PrefabInstanceBindingCacheComponentHandle(cacheAny.Index, cacheAny.Generation);
            cache = PrefabInstanceBindingCacheComponent.Api.FromHandle(_propertyWorld, cacheHandle);
        }
        else
        {
            var created = PrefabInstanceBindingCacheComponent.Api.Create(_propertyWorld, default(PrefabInstanceBindingCacheComponent));
            cache = created;
            SetComponentWithStableId(instanceEntity, new AnyComponentHandle(PrefabInstanceBindingCacheComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation));
        }

        if (!cache.IsAlive)
        {
            instance.SourcePrefabBindingsRevisionAtBuild = bindingsRevision;
            return;
        }

        EntityId sourcePrefabEntity = _world.GetEntityByStableId(sourcePrefabStableId);
        if (sourcePrefabEntity.IsNull || _world.GetNodeType(sourcePrefabEntity) != UiNodeType.Prefab)
        {
            cache.Count = 0;
            instance.SourcePrefabBindingsRevisionAtBuild = bindingsRevision;
            return;
        }

        if (!_world.TryGetComponent(sourcePrefabEntity, PrefabBindingsComponent.Api.PoolIdConst, out AnyComponentHandle bindingsAny) || !bindingsAny.IsValid)
        {
            cache.Count = 0;
            instance.SourcePrefabBindingsRevisionAtBuild = bindingsRevision;
            return;
        }

        var bindingsHandle = new PrefabBindingsComponentHandle(bindingsAny.Index, bindingsAny.Generation);
        var bindings = PrefabBindingsComponent.Api.FromHandle(_propertyWorld, bindingsHandle);
        if (!bindings.IsAlive || bindings.BindingCount == 0)
        {
            cache.Count = 0;
            instance.SourcePrefabBindingsRevisionAtBuild = bindingsRevision;
            return;
        }

        uint instanceStableId = _world.GetStableId(instanceEntity);
        if (instanceStableId == 0)
        {
            cache.Count = 0;
            instance.SourcePrefabBindingsRevisionAtBuild = bindingsRevision;
            return;
        }

        Span<uint> sourceIdToCloneIdSource = stackalloc uint[PrefabInstanceBindingCacheComponent.MaxResolvedBindings];
        Span<uint> sourceIdToCloneIdClone = stackalloc uint[PrefabInstanceBindingCacheComponent.MaxResolvedBindings];
        int mapCount = 0;

        var nodes = new List<EntityId>(capacity: 64);
        nodes.Add(instanceEntity);
        for (int i = 0; i < nodes.Count; i++)
        {
            EntityId current = nodes[i];
            ReadOnlySpan<EntityId> children = _world.GetChildren(current);
            for (int childIndex = 0; childIndex < children.Length; childIndex++)
            {
                nodes.Add(children[childIndex]);
            }

            if (!_world.TryGetComponent(current, PrefabExpandedComponent.Api.PoolIdConst, out AnyComponentHandle expandedAny) || !expandedAny.IsValid)
            {
                continue;
            }

            var expandedHandle = new PrefabExpandedComponentHandle(expandedAny.Index, expandedAny.Generation);
            var expanded = PrefabExpandedComponent.Api.FromHandle(_propertyWorld, expandedHandle);
            if (!expanded.IsAlive || expanded.SourceNodeStableId == 0 || expanded.InstanceRootStableId != instanceStableId)
            {
                continue;
            }

            uint srcId = expanded.SourceNodeStableId;
            uint cloneId = _world.GetStableId(current);
            if (cloneId == 0)
            {
                continue;
            }

            bool exists = false;
            for (int m = 0; m < mapCount; m++)
            {
                if (sourceIdToCloneIdSource[m] == srcId)
                {
                    exists = true;
                    break;
                }
            }
            if (exists)
            {
                continue;
            }

            if (mapCount >= PrefabInstanceBindingCacheComponent.MaxResolvedBindings)
            {
                break;
            }

            sourceIdToCloneIdSource[mapCount] = srcId;
            sourceIdToCloneIdClone[mapCount] = cloneId;
            mapCount++;
        }

        ushort bindingCount = bindings.BindingCount;
        if (bindingCount > PrefabBindingsComponent.MaxBindings)
        {
            bindingCount = PrefabBindingsComponent.MaxBindings;
        }

        ReadOnlySpan<ushort> bindVarId = bindings.VariableIdReadOnlySpan();
        ReadOnlySpan<byte> bindDirection = bindings.DirectionReadOnlySpan();
        ReadOnlySpan<uint> bindTargetSourceNode = bindings.TargetSourceNodeStableIdReadOnlySpan();
        ReadOnlySpan<ushort> bindTargetComponentKind = bindings.TargetComponentKindReadOnlySpan();
        ReadOnlySpan<ushort> bindPropertyIndex = bindings.PropertyIndexHintReadOnlySpan();
        ReadOnlySpan<ulong> bindPropertyId = bindings.PropertyIdReadOnlySpan();
        ReadOnlySpan<ushort> bindPropertyKind = bindings.PropertyKindReadOnlySpan();

        Span<ushort> cacheVarId = cache.VariableIdSpan();
        Span<byte> cacheDirection = cache.DirectionSpan();
        Span<uint> cacheTargetEntity = cache.TargetEntityStableIdSpan();
        Span<ushort> cacheComponentKind = cache.ComponentKindSpan();
        Span<ushort> cachePropertyIndex = cache.PropertyIndexSpan();
        Span<ulong> cachePropertyId = cache.PropertyIdSpan();
        Span<ushort> cachePropertyKind = cache.PropertyKindSpan();

        int outCount = 0;
        for (int i = 0; i < bindingCount; i++)
        {
            ushort variableId = bindVarId[i];
            if (variableId == 0)
            {
                continue;
            }

            uint targetSourceId = bindTargetSourceNode[i];
            uint targetEntityStableId = 0;
            for (int m = 0; m < mapCount; m++)
            {
                if (sourceIdToCloneIdSource[m] == targetSourceId)
                {
                    targetEntityStableId = sourceIdToCloneIdClone[m];
                    break;
                }
            }

            if (targetEntityStableId == 0)
            {
                continue;
            }

            if (outCount >= PrefabInstanceBindingCacheComponent.MaxResolvedBindings)
            {
                break;
            }

            cacheVarId[outCount] = variableId;
            cacheDirection[outCount] = bindDirection[i];
            cacheTargetEntity[outCount] = targetEntityStableId;
            cacheComponentKind[outCount] = bindTargetComponentKind[i];
            cachePropertyIndex[outCount] = bindPropertyIndex[i];
            cachePropertyId[outCount] = bindPropertyId[i];
            cachePropertyKind[outCount] = bindPropertyKind[i];
            outCount++;
        }

        for (int i = outCount; i < PrefabInstanceBindingCacheComponent.MaxResolvedBindings; i++)
        {
            cacheVarId[i] = 0;
            cacheDirection[i] = 0;
            cacheTargetEntity[i] = 0;
            cacheComponentKind[i] = 0;
            cachePropertyIndex[i] = 0;
            cachePropertyId[i] = 0;
            cachePropertyKind[i] = 0;
        }

        cache.Count = (ushort)outCount;
        instance.SourcePrefabBindingsRevisionAtBuild = bindingsRevision;
    }

    private void ApplyPrefabInstanceBindings(EntityId instanceEntity, PrefabInstanceComponent.ViewProxy instance, PrefabVariablesComponent.ViewProxy sourcePrefabVars)
    {
        if (instanceEntity.IsNull)
        {
            return;
        }

        if (!_world.TryGetComponent(instanceEntity, PrefabInstanceBindingCacheComponent.Api.PoolIdConst, out AnyComponentHandle cacheAny) || !cacheAny.IsValid)
        {
            return;
        }

        var cacheHandle = new PrefabInstanceBindingCacheComponentHandle(cacheAny.Index, cacheAny.Generation);
        var cache = PrefabInstanceBindingCacheComponent.Api.FromHandle(_propertyWorld, cacheHandle);
        if (!cache.IsAlive || cache.Count == 0)
        {
            return;
        }

        ushort count = cache.Count;
        if (count > PrefabInstanceBindingCacheComponent.MaxResolvedBindings)
        {
            count = PrefabInstanceBindingCacheComponent.MaxResolvedBindings;
        }

        ReadOnlySpan<ushort> cacheVarId = cache.VariableIdReadOnlySpan();
        ReadOnlySpan<byte> cacheDirection = cache.DirectionReadOnlySpan();
        ReadOnlySpan<uint> cacheTargetEntity = cache.TargetEntityStableIdReadOnlySpan();
        ReadOnlySpan<ushort> cacheComponentKind = cache.ComponentKindReadOnlySpan();
        ReadOnlySpan<ushort> cachePropertyIndex = cache.PropertyIndexReadOnlySpan();
        ReadOnlySpan<ulong> cachePropertyId = cache.PropertyIdReadOnlySpan();
        ReadOnlySpan<ushort> cachePropertyKind = cache.PropertyKindReadOnlySpan();

        ReadOnlySpan<ushort> instanceVarId = instance.VariableIdReadOnlySpan();
        ReadOnlySpan<PropertyValue> instanceValue = instance.ValueReadOnlySpan();
        ushort instanceCount = instance.ValueCount;
        if (instanceCount > PrefabInstanceComponent.MaxVariables)
        {
            instanceCount = PrefabInstanceComponent.MaxVariables;
        }

        ulong overrideMask = instance.OverrideMask;

        Span<ushort> computedVarId = stackalloc ushort[PrefabInstanceBindingCacheComponent.MaxResolvedBindings];
        Span<PropertyValue> computedVarValue = stackalloc PropertyValue[PrefabInstanceBindingCacheComponent.MaxResolvedBindings];
        int computedCount = 0;

        // Pass 1: Property -> Variable (bind-to-source).
        for (int i = 0; i < count; i++)
        {
            ushort variableId = cacheVarId[i];
            if (variableId == 0)
            {
                continue;
            }
            if (cacheDirection[i] != 1)
            {
                continue;
            }

            uint targetStableId = cacheTargetEntity[i];
            if (targetStableId == 0)
            {
                continue;
            }

            EntityId target = _world.GetEntityByStableId(targetStableId);
            if (target.IsNull)
            {
                continue;
            }
            if (!IsDescendantOrSelf(_world, target, instanceEntity))
            {
                continue;
            }

            ushort componentKind = cacheComponentKind[i];
            if (componentKind == 0)
            {
                continue;
            }

            if (!_world.TryGetComponent(target, componentKind, out AnyComponentHandle componentAny) || !componentAny.IsValid)
            {
                continue;
            }

            PropertyKind kind = (PropertyKind)cachePropertyKind[i];
            if (!TryResolveBoundSlot(componentAny, cachePropertyIndex[i], cachePropertyId[i], kind, out PropertySlot slot))
            {
                continue;
            }

            PropertyValue value = ReadBoundValue(slot, kind);

            int existingIndex = -1;
            for (int j = 0; j < computedCount; j++)
            {
                if (computedVarId[j] == variableId)
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex < 0)
            {
                if (computedCount >= computedVarId.Length)
                {
                    continue;
                }
                existingIndex = computedCount++;
                computedVarId[existingIndex] = variableId;
            }

            computedVarValue[existingIndex] = value;
        }

        for (int i = 0; i < count; i++)
        {
            ushort variableId = cacheVarId[i];
            if (variableId == 0)
            {
                continue;
            }
            if (cacheDirection[i] != 0)
            {
                continue;
            }

            PropertyValue value = default;
            bool foundComputed = false;
            for (int j = 0; j < computedCount; j++)
            {
                if (computedVarId[j] == variableId)
                {
                    value = computedVarValue[j];
                    foundComputed = true;
                    break;
                }
            }
            if (!foundComputed)
            {
                if (!TryGetPrefabInstanceVariableValue(instanceVarId, instanceValue, instanceCount, overrideMask, sourcePrefabVars, variableId, out value))
                {
                    continue;
                }
            }

            uint targetStableId = cacheTargetEntity[i];
            if (targetStableId == 0)
            {
                continue;
            }

            EntityId target = _world.GetEntityByStableId(targetStableId);
            if (target.IsNull)
            {
                continue;
            }
            if (!IsDescendantOrSelf(_world, target, instanceEntity))
            {
                continue;
            }

            ushort componentKind = cacheComponentKind[i];
            if (componentKind == 0)
            {
                continue;
            }

            if (!_world.TryGetComponent(target, componentKind, out AnyComponentHandle componentAny) || !componentAny.IsValid)
            {
                continue;
            }

            PropertyKind kind = (PropertyKind)cachePropertyKind[i];
            if (!TryResolveBoundSlot(componentAny, cachePropertyIndex[i], cachePropertyId[i], kind, out PropertySlot slot))
            {
                continue;
            }

            WriteBoundValue(slot, kind, value);
        }
    }

    private void ProcessListLayoutBindings(EntityId instanceEntity, PrefabInstanceComponent.ViewProxy instance, PrefabVariablesComponent.ViewProxy sourcePrefabVars)
    {
        if (instanceEntity.IsNull)
        {
            return;
        }

        if (!_world.TryGetComponent(instanceEntity, PrefabInstanceBindingCacheComponent.Api.PoolIdConst, out AnyComponentHandle cacheAny) || !cacheAny.IsValid)
        {
            return;
        }

        var cacheHandle = new PrefabInstanceBindingCacheComponentHandle(cacheAny.Index, cacheAny.Generation);
        var cache = PrefabInstanceBindingCacheComponent.Api.FromHandle(_propertyWorld, cacheHandle);
        if (!cache.IsAlive || cache.Count == 0)
        {
            return;
        }

        const byte DirectionListBind = 2;

        ushort count = cache.Count;
        if (count > PrefabInstanceBindingCacheComponent.MaxResolvedBindings)
        {
            count = PrefabInstanceBindingCacheComponent.MaxResolvedBindings;
        }

        ReadOnlySpan<ushort> cacheVarId = cache.VariableIdReadOnlySpan();
        ReadOnlySpan<byte> cacheDirection = cache.DirectionReadOnlySpan();
        ReadOnlySpan<uint> cacheTargetEntity = cache.TargetEntityStableIdReadOnlySpan();

        ReadOnlySpan<ushort> instanceVarId = instance.VariableIdReadOnlySpan();
        ReadOnlySpan<PropertyValue> instanceValue = instance.ValueReadOnlySpan();
        ushort instanceCount = instance.ValueCount;
        if (instanceCount > PrefabInstanceComponent.MaxVariables)
        {
            instanceCount = PrefabInstanceComponent.MaxVariables;
        }

        ulong overrideMask = instance.OverrideMask;

        for (int bindIndex = 0; bindIndex < count; bindIndex++)
        {
            if (cacheDirection[bindIndex] != DirectionListBind)
            {
                continue;
            }

            ushort variableId = cacheVarId[bindIndex];
            if (variableId == 0)
            {
                continue;
            }

            uint layoutStableId = cacheTargetEntity[bindIndex];
            if (layoutStableId == 0)
            {
                continue;
            }

            EntityId layoutEntity = _world.GetEntityByStableId(layoutStableId);
            if (layoutEntity.IsNull)
            {
                continue;
            }

            if (!IsDescendantOrSelf(_world, layoutEntity, instanceEntity))
            {
                continue;
            }

            int instanceVarIndex = -1;
            for (int i = 0; i < instanceCount; i++)
            {
                if (instanceVarId[i] == variableId)
                {
                    instanceVarIndex = i;
                    break;
                }
            }

            bool listOverridden = instanceVarIndex >= 0 && (overrideMask & (1UL << instanceVarIndex)) != 0;

            if (!TryGetPrefabInstanceVariableValue(instanceVarId, instanceValue, instanceCount, overrideMask, sourcePrefabVars, variableId, out PropertyValue listVarValue))
            {
                continue;
            }

            uint typePrefabStableId = listVarValue.UInt;
            if (typePrefabStableId == 0)
            {
                DestroyAllListGeneratedChildren(layoutEntity, layoutStableId, variableId);
                continue;
            }

            if (instance.SourcePrefabStableId != 0 && typePrefabStableId == instance.SourcePrefabStableId)
            {
                DestroyAllListGeneratedChildren(layoutEntity, layoutStableId, variableId);
                continue;
            }

            EntityId typePrefabEntity = _world.GetEntityByStableId(typePrefabStableId);
            if (typePrefabEntity.IsNull || _world.GetNodeType(typePrefabEntity) != UiNodeType.Prefab)
            {
                DestroyAllListGeneratedChildren(layoutEntity, layoutStableId, variableId);
                continue;
            }

            PrefabVariablesComponent.ViewProxy typeVars = default;
            if (_world.TryGetComponent(typePrefabEntity, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle typeVarsAny) && typeVarsAny.IsValid)
            {
                var typeVarsHandle = new PrefabVariablesComponentHandle(typeVarsAny.Index, typeVarsAny.Generation);
                typeVars = PrefabVariablesComponent.Api.FromHandle(_propertyWorld, typeVarsHandle);
            }

            // Resolve list data (instance override or source prefab defaults).
            PrefabListDataComponent.ViewProxy listData = default;
            int listEntryIndex = -1;
            if (listOverridden &&
                _world.TryGetComponent(instanceEntity, PrefabListDataComponent.Api.PoolIdConst, out AnyComponentHandle listAny) &&
                listAny.IsValid)
            {
                var handle = new PrefabListDataComponentHandle(listAny.Index, listAny.Generation);
                var view = PrefabListDataComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive && TryFindListEntryIndex(view, variableId, out int idx))
                {
                    listData = view;
                    listEntryIndex = idx;
                }
            }

            if (listEntryIndex < 0 && instance.SourcePrefabStableId != 0)
            {
                EntityId sourcePrefabEntity = _world.GetEntityByStableId(instance.SourcePrefabStableId);
                if (!sourcePrefabEntity.IsNull &&
                    _world.TryGetComponent(sourcePrefabEntity, PrefabListDataComponent.Api.PoolIdConst, out AnyComponentHandle srcListAny) &&
                    srcListAny.IsValid)
                {
                    var handle = new PrefabListDataComponentHandle(srcListAny.Index, srcListAny.Generation);
                    var view = PrefabListDataComponent.Api.FromHandle(_propertyWorld, handle);
                    if (view.IsAlive && TryFindListEntryIndex(view, variableId, out int idx))
                    {
                        listData = view;
                        listEntryIndex = idx;
                    }
                }
            }

            int itemCount = 0;
            int itemStart = 0;
            int stride = 0;
            ReadOnlySpan<PropertyValue> flatItems = default;
            if (listEntryIndex >= 0 && listData.IsAlive)
            {
                ReadOnlySpan<ushort> entryItemCount = listData.EntryItemCountReadOnlySpan();
                ReadOnlySpan<ushort> entryItemStart = listData.EntryItemStartReadOnlySpan();
                ReadOnlySpan<ushort> entryFieldCount = listData.EntryFieldCountReadOnlySpan();
                flatItems = listData.ItemsReadOnlySpan();
                itemCount = entryItemCount[listEntryIndex];
                itemStart = entryItemStart[listEntryIndex];
                stride = entryFieldCount[listEntryIndex];
            }

            SyncListGeneratedChildren(
                layoutEntity,
                layoutStableId,
                variableId,
                typePrefabStableId,
                typeVars,
                itemCount,
                itemStart,
                stride,
                flatItems);
        }
    }

    private void SyncListGeneratedChildren(
        EntityId layoutEntity,
        uint layoutStableId,
        ushort listVariableId,
        uint typePrefabStableId,
        PrefabVariablesComponent.ViewProxy typeVars,
        int itemCount,
        int itemStart,
        int stride,
        ReadOnlySpan<PropertyValue> flatItems)
    {
        if (layoutEntity.IsNull)
        {
            return;
        }

        if (itemCount < 0)
        {
            itemCount = 0;
        }
        if (itemCount > PrefabListDataComponent.MaxItemsPerList)
        {
            itemCount = PrefabListDataComponent.MaxItemsPerList;
        }

        Span<EntityId> byIndex = stackalloc EntityId[PrefabListDataComponent.MaxItemsPerList];
        for (int i = 0; i < byIndex.Length; i++)
        {
            byIndex[i] = EntityId.Null;
        }

        Span<EntityId> toDestroy = stackalloc EntityId[64];
        int destroyCount = 0;

        ReadOnlySpan<EntityId> children = _world.GetChildren(layoutEntity);
        for (int i = 0; i < children.Length; i++)
        {
            EntityId child = children[i];
            if (!_world.TryGetComponent(child, ListGeneratedComponent.Api.PoolIdConst, out AnyComponentHandle tagAny) || !tagAny.IsValid)
            {
                continue;
            }

            var tagHandle = new ListGeneratedComponentHandle(tagAny.Index, tagAny.Generation);
            var tag = ListGeneratedComponent.Api.FromHandle(_propertyWorld, tagHandle);
            if (!tag.IsAlive)
            {
                continue;
            }

            if (tag.SourceLayoutStableId != layoutStableId || tag.ListVariableId != listVariableId)
            {
                continue;
            }

            int idx = tag.ItemIndex;
            if ((uint)idx >= (uint)itemCount || (uint)idx >= (uint)byIndex.Length || !byIndex[idx].IsNull)
            {
                if (destroyCount < toDestroy.Length)
                {
                    toDestroy[destroyCount++] = child;
                }
                continue;
            }

            byIndex[idx] = child;
        }

        for (int i = 0; i < destroyCount; i++)
        {
            _world.DestroyEntity(toDestroy[i]);
        }

        // Build schema mapping: fieldIndex -> prefab variable array index.
        Span<int> schemaVarIndex = stackalloc int[PrefabListDataComponent.MaxFieldsPerItem];
        int schemaCount = 0;
        if (typeVars.IsAlive && typeVars.VariableCount > 0)
        {
            ushort varCount = typeVars.VariableCount;
            if (varCount > PrefabVariablesComponent.MaxVariables)
            {
                varCount = (ushort)PrefabVariablesComponent.MaxVariables;
            }

            ReadOnlySpan<ushort> ids = typeVars.VariableIdReadOnlySpan();
            ReadOnlySpan<int> kinds = typeVars.KindReadOnlySpan();
            for (int i = 0; i < varCount && schemaCount < schemaVarIndex.Length; i++)
            {
                if (ids[i] == 0)
                {
                    continue;
                }

                PropertyKind kind = (PropertyKind)kinds[i];
                if (kind == PropertyKind.List)
                {
                    continue;
                }

                schemaVarIndex[schemaCount++] = i;
            }
        }

        int fieldCount = schemaCount;
        if (stride < fieldCount)
        {
            fieldCount = stride;
        }
        if (fieldCount < 0)
        {
            fieldCount = 0;
        }

        for (int idx = 0; idx < itemCount; idx++)
        {
            EntityId child = byIndex[idx];
            if (child.IsNull)
            {
                child = _world.CreateEntity(UiNodeType.PrefabInstance, layoutEntity);
                byIndex[idx] = child;
            }

            EnsureListGeneratedTag(child, layoutStableId, listVariableId, (ushort)idx);
            EnsurePrefabInstanceScaffold(child);
            EnsurePrefabInstanceComponent(child, typePrefabStableId);
            EnsurePrefabInstanceCanvasComponent(child, typePrefabStableId);

            if (!_world.TryGetComponent(child, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) || !instanceAny.IsValid)
            {
                continue;
            }

            var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var instance = PrefabInstanceComponent.Api.FromHandle(_propertyWorld, instanceHandle);
            if (!instance.IsAlive)
            {
                continue;
            }

            if (typeVars.IsAlive)
            {
                if (NeedsPrefabInstanceVariableSync(instance, typeVars))
                {
                    SyncPrefabInstanceVariables(instance, typeVars);
                }

                Span<PropertyValue> values = instance.ValueSpan();
                ulong mask = instance.OverrideMask;

                for (int f = 0; f < fieldCount; f++)
                {
                    int varIndex = schemaVarIndex[f];
                    int listIndex = itemStart + idx * stride + f;
                    if ((uint)listIndex >= (uint)flatItems.Length)
                    {
                        continue;
                    }

                    if ((uint)varIndex >= (uint)values.Length)
                    {
                        continue;
                    }

                    values[varIndex] = flatItems[listIndex];
                    mask |= 1UL << varIndex;
                }

                instance.OverrideMask = mask;
            }
        }

        // Destroy any remaining tagged children if list shrank.
        for (int i = itemCount; i < byIndex.Length; i++)
        {
            if (!byIndex[i].IsNull)
            {
                _world.DestroyEntity(byIndex[i]);
            }
        }
    }

    private void DestroyAllListGeneratedChildren(EntityId layoutEntity, uint layoutStableId, ushort listVariableId)
    {
        if (layoutEntity.IsNull)
        {
            return;
        }

        ReadOnlySpan<EntityId> children = _world.GetChildren(layoutEntity);
        if (children.Length == 0)
        {
            return;
        }

        EntityId[] scratch = ArrayPool<EntityId>.Shared.Rent(children.Length);
        int destroyCount = 0;
        try
        {
            for (int i = 0; i < children.Length; i++)
            {
                EntityId child = children[i];
                if (!_world.TryGetComponent(child, ListGeneratedComponent.Api.PoolIdConst, out AnyComponentHandle tagAny) || !tagAny.IsValid)
                {
                    continue;
                }

                var tagHandle = new ListGeneratedComponentHandle(tagAny.Index, tagAny.Generation);
                var tag = ListGeneratedComponent.Api.FromHandle(_propertyWorld, tagHandle);
                if (!tag.IsAlive)
                {
                    continue;
                }

                if (tag.SourceLayoutStableId == layoutStableId && tag.ListVariableId == listVariableId)
                {
                    scratch[destroyCount++] = child;
                }
            }

            for (int i = 0; i < destroyCount; i++)
            {
                _world.DestroyEntity(scratch[i]);
            }
        }
        finally
        {
            ArrayPool<EntityId>.Shared.Return(scratch);
        }
    }

    private void EnsureListGeneratedTag(EntityId entity, uint layoutStableId, ushort listVariableId, ushort itemIndex)
    {
        if (_world.TryGetComponent(entity, ListGeneratedComponent.Api.PoolIdConst, out AnyComponentHandle tagAny) && tagAny.IsValid)
        {
            var handle = new ListGeneratedComponentHandle(tagAny.Index, tagAny.Generation);
            var view = ListGeneratedComponent.Api.FromHandle(_propertyWorld, handle);
            if (view.IsAlive)
            {
                view.SourceLayoutStableId = layoutStableId;
                view.ListVariableId = listVariableId;
                view.ItemIndex = itemIndex;
            }
            return;
        }

        var created = ListGeneratedComponent.Api.Create(_propertyWorld, new ListGeneratedComponent
        {
            SourceLayoutStableId = layoutStableId,
            ListVariableId = listVariableId,
            ItemIndex = itemIndex
        });
        SetComponentWithStableId(entity, new AnyComponentHandle(ListGeneratedComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation));
    }

    private void EnsurePrefabInstanceScaffold(EntityId entity)
    {
        EnsureTransformComponent(entity);
        EnsureBlendComponent(entity);
    }

    private void EnsurePrefabInstanceCanvasComponent(EntityId instanceEntity, EntityId sourcePrefabEntity)
    {
        if (instanceEntity.IsNull || sourcePrefabEntity.IsNull)
        {
            return;
        }

        if (!_world.TryGetComponent(sourcePrefabEntity, PrefabCanvasComponent.Api.PoolIdConst, out AnyComponentHandle sourceCanvasAny) || !sourceCanvasAny.IsValid)
        {
            return;
        }

        var sourceCanvasHandle = new PrefabCanvasComponentHandle(sourceCanvasAny.Index, sourceCanvasAny.Generation);
        var sourceCanvas = PrefabCanvasComponent.Api.FromHandle(_propertyWorld, sourceCanvasHandle);
        if (!sourceCanvas.IsAlive)
        {
            return;
        }

        bool allowSyncFromSource = true;
        if (_world.TryGetComponent(instanceEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) && instanceAny.IsValid)
        {
            var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var instance = PrefabInstanceComponent.Api.FromHandle(_propertyWorld, instanceHandle);
            if (instance.IsAlive && instance.CanvasSizeIsOverridden != 0)
            {
                allowSyncFromSource = false;
            }
        }

        if (_world.TryGetComponent(instanceEntity, PrefabCanvasComponent.Api.PoolIdConst, out AnyComponentHandle instanceCanvasAny) && instanceCanvasAny.IsValid)
        {
            var handle = new PrefabCanvasComponentHandle(instanceCanvasAny.Index, instanceCanvasAny.Generation);
            var view = PrefabCanvasComponent.Api.FromHandle(_propertyWorld, handle);
            if (view.IsAlive)
            {
                if (view.Size.X <= 0f || view.Size.Y <= 0f)
                {
                    view.Size = sourceCanvas.Size;
                }
                else if (allowSyncFromSource && view.Size != sourceCanvas.Size)
                {
                    // Keep default instance size in sync with its source prefab unless explicitly overridden.
                    // Parent layout may overwrite Size later in the frame.
                    view.Size = sourceCanvas.Size;
                }
                return;
            }
        }

        var created = PrefabCanvasComponent.Api.Create(_propertyWorld, new PrefabCanvasComponent { Size = sourceCanvas.Size });
        SetComponentWithStableId(instanceEntity, PrefabCanvasComponentProperties.ToAnyHandle(created.Handle));
    }

    private void EnsurePrefabInstanceCanvasComponent(EntityId instanceEntity, uint sourcePrefabStableId)
    {
        if (instanceEntity.IsNull || sourcePrefabStableId == 0)
        {
            return;
        }

        EntityId sourcePrefabEntity = _world.GetEntityByStableId(sourcePrefabStableId);
        if (sourcePrefabEntity.IsNull || _world.GetNodeType(sourcePrefabEntity) != UiNodeType.Prefab)
        {
            return;
        }

        EnsurePrefabInstanceCanvasComponent(instanceEntity, sourcePrefabEntity);
    }

    private void EnsureTransformComponent(EntityId entity)
    {
        if (_world.TryGetComponent(entity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny) && transformAny.IsValid)
        {
            return;
        }

        var created = TransformComponent.Api.Create(_propertyWorld, new TransformComponent
        {
            Position = Vector2.Zero,
            Scale = Vector2.One,
            Rotation = 0f,
            Anchor = Vector2.Zero,
            Depth = 0f
        });
        SetComponentWithStableId(entity, TransformComponentProperties.ToAnyHandle(created.Handle));
    }

    private void EnsureBlendComponent(EntityId entity)
    {
        if (_world.TryGetComponent(entity, BlendComponent.Api.PoolIdConst, out AnyComponentHandle blendAny) && blendAny.IsValid)
        {
            return;
        }

        var created = BlendComponent.Api.Create(_propertyWorld, new BlendComponent
        {
            IsVisible = true,
            Opacity = 1f,
            BlendMode = (int)PaintBlendMode.Normal
        });
        SetComponentWithStableId(entity, BlendComponentProperties.ToAnyHandle(created.Handle));
    }

    private void EnsurePrefabInstanceComponent(EntityId entity, uint sourcePrefabStableId)
    {
        if (_world.TryGetComponent(entity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) && instanceAny.IsValid)
        {
            var handle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var view = PrefabInstanceComponent.Api.FromHandle(_propertyWorld, handle);
            if (view.IsAlive && view.SourcePrefabStableId != sourcePrefabStableId)
            {
                view.SourcePrefabStableId = sourcePrefabStableId;
                view.SourcePrefabRevisionAtBuild = 0;
                view.SourcePrefabBindingsRevisionAtBuild = 0;
                view.CanvasSizeIsOverridden = 0;
            }
            return;
        }

        var created = PrefabInstanceComponent.Api.Create(_propertyWorld, new PrefabInstanceComponent
        {
            SourcePrefabStableId = sourcePrefabStableId,
            SourcePrefabRevisionAtBuild = 0,
            SourcePrefabBindingsRevisionAtBuild = 0,
            ValueCount = 0,
            OverrideMask = 0
            ,CanvasSizeIsOverridden = 0
        });
        SetComponentWithStableId(entity, new AnyComponentHandle(PrefabInstanceComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation));
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

    private static bool IsDescendantOrSelf(UiWorld world, EntityId entity, EntityId possibleAncestor)
    {
        if (entity.Value == possibleAncestor.Value)
        {
            return true;
        }

        EntityId current = world.GetParent(entity);
        int guard = 512;
        while (!current.IsNull && guard-- > 0)
        {
            if (current.Value == possibleAncestor.Value)
            {
                return true;
            }

            current = world.GetParent(current);
        }

        return false;
    }

    private static bool TryGetPrefabInstanceVariableValue(
        ReadOnlySpan<ushort> instanceVariableId,
        ReadOnlySpan<PropertyValue> instanceValue,
        ushort instanceCount,
        ulong overrideMask,
        PrefabVariablesComponent.ViewProxy sourcePrefabVars,
        ushort variableId,
        out PropertyValue value)
    {
        value = default;

        int instanceIndex = -1;
        for (int i = 0; i < instanceCount; i++)
        {
            if (instanceVariableId[i] == variableId)
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
                value = instanceValue[instanceIndex];
                return true;
            }
        }

        if (!sourcePrefabVars.IsAlive || sourcePrefabVars.VariableCount == 0)
        {
            if (instanceIndex >= 0)
            {
                value = instanceValue[instanceIndex];
                return true;
            }

            return false;
        }

        ReadOnlySpan<ushort> prefabVarId = sourcePrefabVars.VariableIdReadOnlySpan();
        ReadOnlySpan<PropertyValue> prefabDefaults = sourcePrefabVars.DefaultValueReadOnlySpan();
        ushort prefabCount = sourcePrefabVars.VariableCount;
        if (prefabCount > PrefabVariablesComponent.MaxVariables)
        {
            prefabCount = (ushort)PrefabVariablesComponent.MaxVariables;
        }

        for (int i = 0; i < prefabCount; i++)
        {
            if (prefabVarId[i] == variableId)
            {
                value = prefabDefaults[i];
                return true;
            }
        }

        if (instanceIndex >= 0)
        {
            value = instanceValue[instanceIndex];
            return true;
        }

        return false;
    }

    private static bool TryResolveBoundSlot(AnyComponentHandle component, ushort propertyIndexHint, ulong propertyId, PropertyKind kind, out PropertySlot slot)
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

    private void WriteBoundValue(in PropertySlot slot, PropertyKind kind, in PropertyValue value)
    {
        switch (kind)
        {
            case PropertyKind.Float:
                PropertyDispatcher.WriteFloat(_propertyWorld, slot, value.Float);
                break;
            case PropertyKind.Int:
                PropertyDispatcher.WriteInt(_propertyWorld, slot, value.Int);
                break;
            case PropertyKind.Bool:
                PropertyDispatcher.WriteBool(_propertyWorld, slot, value.Bool);
                break;
            case PropertyKind.Vec2:
                PropertyDispatcher.WriteVec2(_propertyWorld, slot, value.Vec2);
                break;
            case PropertyKind.Vec3:
                PropertyDispatcher.WriteVec3(_propertyWorld, slot, value.Vec3);
                break;
            case PropertyKind.Vec4:
                PropertyDispatcher.WriteVec4(_propertyWorld, slot, value.Vec4);
                break;
            case PropertyKind.Color32:
                PropertyDispatcher.WriteColor32(_propertyWorld, slot, value.Color32);
                break;
            case PropertyKind.StringHandle:
                PropertyDispatcher.WriteStringHandle(_propertyWorld, slot, value.StringHandle);
                break;
            case PropertyKind.Fixed64:
                PropertyDispatcher.WriteFixed64(_propertyWorld, slot, value.Fixed64);
                break;
            case PropertyKind.Fixed64Vec2:
                PropertyDispatcher.WriteFixed64Vec2(_propertyWorld, slot, value.Fixed64Vec2);
                break;
            case PropertyKind.Fixed64Vec3:
                PropertyDispatcher.WriteFixed64Vec3(_propertyWorld, slot, value.Fixed64Vec3);
                break;
        }
    }

    private PropertyValue ReadBoundValue(in PropertySlot slot, PropertyKind kind)
    {
        switch (kind)
        {
            case PropertyKind.Float:
                return PropertyValue.FromFloat(PropertyDispatcher.ReadFloat(_propertyWorld, slot));
            case PropertyKind.Int:
                return PropertyValue.FromInt(PropertyDispatcher.ReadInt(_propertyWorld, slot));
            case PropertyKind.Bool:
                return PropertyValue.FromBool(PropertyDispatcher.ReadBool(_propertyWorld, slot));
            case PropertyKind.Vec2:
                return PropertyValue.FromVec2(PropertyDispatcher.ReadVec2(_propertyWorld, slot));
            case PropertyKind.Vec3:
                return PropertyValue.FromVec3(PropertyDispatcher.ReadVec3(_propertyWorld, slot));
            case PropertyKind.Vec4:
                return PropertyValue.FromVec4(PropertyDispatcher.ReadVec4(_propertyWorld, slot));
            case PropertyKind.Color32:
                return PropertyValue.FromColor32(PropertyDispatcher.ReadColor32(_propertyWorld, slot));
            case PropertyKind.StringHandle:
                return PropertyValue.FromStringHandle(PropertyDispatcher.ReadStringHandle(_propertyWorld, slot));
            case PropertyKind.Fixed64:
                return PropertyValue.FromFixed64(PropertyDispatcher.ReadFixed64(_propertyWorld, slot));
            case PropertyKind.Fixed64Vec2:
                return PropertyValue.FromFixed64Vec2(PropertyDispatcher.ReadFixed64Vec2(_propertyWorld, slot));
            case PropertyKind.Fixed64Vec3:
                return PropertyValue.FromFixed64Vec3(PropertyDispatcher.ReadFixed64Vec3(_propertyWorld, slot));
            default:
                return default;
        }
    }

    private static bool NeedsPrefabInstanceVariableSync(PrefabInstanceComponent.ViewProxy instance, PrefabVariablesComponent.ViewProxy prefabVars)
    {
        ushort prefabCount = prefabVars.VariableCount;
        if (prefabCount > PrefabInstanceComponent.MaxVariables)
        {
            prefabCount = PrefabInstanceComponent.MaxVariables;
        }

        ushort instanceCount = instance.ValueCount;
        if (instanceCount != prefabCount)
        {
            return true;
        }

        if (prefabCount == 0)
        {
            return false;
        }

        ReadOnlySpan<ushort> prefabIds = prefabVars.VariableIdReadOnlySpan();
        ReadOnlySpan<ushort> instanceIds = instance.VariableIdReadOnlySpan();

        for (int i = 0; i < prefabCount; i++)
        {
            if (prefabIds[i] != instanceIds[i])
            {
                return true;
            }
        }

        return false;
    }

    private static void SyncPrefabInstanceVariables(PrefabInstanceComponent.ViewProxy instance, PrefabVariablesComponent.ViewProxy prefabVars)
    {
        ushort prefabCount = prefabVars.VariableCount;
        if (prefabCount > PrefabInstanceComponent.MaxVariables)
        {
            prefabCount = PrefabInstanceComponent.MaxVariables;
        }

        ReadOnlySpan<ushort> prefabIds = prefabVars.VariableIdReadOnlySpan();
        ReadOnlySpan<PropertyValue> prefabDefaults = prefabVars.DefaultValueReadOnlySpan();

        ReadOnlySpan<ushort> srcInstanceIds = instance.VariableIdReadOnlySpan();
        ReadOnlySpan<PropertyValue> srcInstanceValues = instance.ValueReadOnlySpan();
        ushort srcCount = instance.ValueCount;
        if (srcCount > PrefabInstanceComponent.MaxVariables)
        {
            srcCount = PrefabInstanceComponent.MaxVariables;
        }

        ulong srcOverrideMask = instance.OverrideMask;
        ulong dstOverrideMask = 0;

        Span<ushort> dstIds = instance.VariableIdSpan();
        Span<PropertyValue> dstValues = instance.ValueSpan();

        for (int i = 0; i < prefabCount; i++)
        {
            ushort id = prefabIds[i];
            dstIds[i] = id;

            PropertyValue value = prefabDefaults[i];
            for (int j = 0; j < srcCount; j++)
            {
                if (srcInstanceIds[j] == id)
                {
                    value = srcInstanceValues[j];
                    if ((srcOverrideMask & (1UL << j)) != 0)
                    {
                        dstOverrideMask |= 1UL << i;
                    }
                    break;
                }
            }
            dstValues[i] = value;
        }

        for (int i = prefabCount; i < PrefabInstanceComponent.MaxVariables; i++)
        {
            dstIds[i] = 0;
            dstValues[i] = default;
        }

        instance.ValueCount = prefabCount;
        instance.OverrideMask = dstOverrideMask;
    }

    private static void TryInferPrefabInstanceOverrideMaskFromDefaults(PrefabInstanceComponent.ViewProxy instance, PrefabVariablesComponent.ViewProxy prefabVars)
    {
        if (!prefabVars.IsAlive || prefabVars.VariableCount == 0)
        {
            return;
        }

        if (instance.OverrideMask != 0)
        {
            return;
        }

        ushort count = prefabVars.VariableCount;
        if (count > PrefabInstanceComponent.MaxVariables)
        {
            count = PrefabInstanceComponent.MaxVariables;
        }

        if (instance.ValueCount != count)
        {
            return;
        }

        ReadOnlySpan<PropertyValue> instanceValue = instance.ValueReadOnlySpan();
        ReadOnlySpan<PropertyValue> defaults = prefabVars.DefaultValueReadOnlySpan();
        ReadOnlySpan<int> kinds = prefabVars.KindReadOnlySpan();

        ulong mask = 0;
        for (int i = 0; i < count; i++)
        {
            var kind = (PropertyKind)kinds[i];
            if (!instanceValue[i].Equals(kind, defaults[i]))
            {
                mask |= 1UL << i;
            }
        }

        if (mask != 0)
        {
            instance.OverrideMask = mask;
        }
    }

    private uint GetPrefabRevision(EntityId prefabEntity)
    {
        if (prefabEntity.IsNull)
        {
            return 0;
        }

        if (!_world.TryGetComponent(prefabEntity, PrefabRevisionComponent.Api.PoolIdConst, out AnyComponentHandle revisionAny))
        {
            var created = PrefabRevisionComponent.Api.Create(_propertyWorld, new PrefabRevisionComponent { Revision = 1 });
            SetComponentWithStableId(prefabEntity, new AnyComponentHandle(PrefabRevisionComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation));
            return 1;
        }

        var handle = new PrefabRevisionComponentHandle(revisionAny.Index, revisionAny.Generation);
        var view = PrefabRevisionComponent.Api.FromHandle(_propertyWorld, handle);
        if (!view.IsAlive)
        {
            return 0;
        }

        return view.Revision;
    }

    internal void BumpPrefabRevision(EntityId prefabEntity)
    {
        if (prefabEntity.IsNull || _world.GetNodeType(prefabEntity) != UiNodeType.Prefab)
        {
            return;
        }

        if (!_world.TryGetComponent(prefabEntity, PrefabRevisionComponent.Api.PoolIdConst, out AnyComponentHandle revisionAny))
        {
            var created = PrefabRevisionComponent.Api.Create(_propertyWorld, new PrefabRevisionComponent { Revision = 1 });
            SetComponentWithStableId(prefabEntity, new AnyComponentHandle(PrefabRevisionComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation));
            return;
        }

        var handle = new PrefabRevisionComponentHandle(revisionAny.Index, revisionAny.Generation);
        var view = PrefabRevisionComponent.Api.FromHandle(_propertyWorld, handle);
        if (!view.IsAlive)
        {
            return;
        }

        view.Revision = view.Revision + 1;
    }

    internal void BumpSelectedPrefabRevision()
    {
        BumpPrefabRevision(_selectedPrefabEntity);
    }

    private bool WouldPrefabInstanceCreateCycle(EntityId instanceEntity, uint sourcePrefabStableId)
    {
        if (sourcePrefabStableId == 0)
        {
            return false;
        }

        EntityId current = _world.GetParent(instanceEntity);
        int guard = 512;
        while (!current.IsNull && guard-- > 0)
        {
            if (_world.GetNodeType(current) == UiNodeType.PrefabInstance)
            {
                if (_world.TryGetComponent(current, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny))
                {
                    var handle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
                    var view = PrefabInstanceComponent.Api.FromHandle(_propertyWorld, handle);
                    if (view.IsAlive && view.SourcePrefabStableId == sourcePrefabStableId)
                    {
                        return true;
                    }
                }
            }

            current = _world.GetParent(current);
        }

        return false;
    }

	    private uint GetPrefabInstanceSelectionRootStableId(EntityId instanceEntity)
	    {
	        if (_world.TryGetComponent(instanceEntity, PrefabExpandedComponent.Api.PoolIdConst, out AnyComponentHandle expandedAny))
	        {
	            var expandedHandle = new PrefabExpandedComponentHandle(expandedAny.Index, expandedAny.Generation);
            var expanded = PrefabExpandedComponent.Api.FromHandle(_propertyWorld, expandedHandle);
            if (expanded.IsAlive && expanded.InstanceRootStableId != 0)
            {
                return expanded.InstanceRootStableId;
            }
        }

	        return _world.GetStableId(instanceEntity);
	    }

	    internal EntityId ResolvePrefabInstanceRootEntity(EntityId entity)
	    {
	        if (entity.IsNull)
	        {
	            return EntityId.Null;
	        }

	        if (!_world.TryGetComponent(entity, PrefabExpandedComponent.Api.PoolIdConst, out AnyComponentHandle expandedAny) || !expandedAny.IsValid)
	        {
	            return entity;
	        }

	        var expandedHandle = new PrefabExpandedComponentHandle(expandedAny.Index, expandedAny.Generation);
	        var expanded = PrefabExpandedComponent.Api.FromHandle(_propertyWorld, expandedHandle);
	        if (!expanded.IsAlive || expanded.InstanceRootStableId == 0)
	        {
	            return entity;
	        }

	        EntityId root = _world.GetEntityByStableId(expanded.InstanceRootStableId);
	        if (root.IsNull)
	        {
	            return entity;
	        }

	        return root;
	    }

	    private EntityId ResolveOpaquePrefabInstanceSelection(EntityId entity)
	    {
	        if (entity.IsNull)
	        {
	            return EntityId.Null;
        }

        if (!_world.TryGetComponent(entity, PrefabExpandedComponent.Api.PoolIdConst, out AnyComponentHandle expandedAny))
        {
            return entity;
        }

        var expandedHandle = new PrefabExpandedComponentHandle(expandedAny.Index, expandedAny.Generation);
        var expanded = PrefabExpandedComponent.Api.FromHandle(_propertyWorld, expandedHandle);
        if (!expanded.IsAlive || expanded.InstanceRootStableId == 0)
        {
            return entity;
        }

        EntityId root = _world.GetEntityByStableId(expanded.InstanceRootStableId);
        if (root.IsNull)
        {
            return entity;
        }

        if (GetLayerHidden(expanded.InstanceRootStableId) || GetLayerLocked(expanded.InstanceRootStableId))
        {
            return EntityId.Null;
        }

        return root;
    }

    private void RebuildPrefabInstanceExpansion(EntityId instanceEntity, EntityId sourcePrefabEntity, uint sourceRevision)
    {
        DestroyAllChildren(instanceEntity);

        uint instanceRootStableId = GetPrefabInstanceSelectionRootStableId(instanceEntity);

        Vector2 instanceCanvasSize = GetPrefabInstanceCanvasSize(instanceEntity, sourcePrefabEntity);
        EntityId expandedRoot = EnsureExpandedRootProxy(instanceEntity, instanceRootStableId, sourcePrefabEntity, instanceCanvasSize);

        byte[] scratch = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            ReadOnlySpan<EntityId> children = _world.GetChildren(sourcePrefabEntity);
            for (int i = 0; i < children.Length; i++)
            {
                EntityId child = children[i];
                ClonePrefabSubtree(child, expandedRoot, instanceRootStableId, ref scratch);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }

        ApplyPrefabInstancePropertyOverridesAfterRebuild(instanceEntity, instanceRootStableId);

        if (_world.TryGetComponent(instanceEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny))
        {
            var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var instance = PrefabInstanceComponent.Api.FromHandle(_propertyWorld, instanceHandle);
            if (instance.IsAlive)
            {
                instance.SourcePrefabRevisionAtBuild = sourceRevision;
            }
        }
    }

    private void ApplyPrefabInstancePropertyOverridesAfterRebuild(EntityId instanceEntity, uint instanceRootStableId)
    {
        if (instanceEntity.IsNull || instanceRootStableId == 0)
        {
            return;
        }

        if (!_world.TryGetComponent(instanceEntity, PrefabInstancePropertyOverridesComponent.Api.PoolIdConst, out AnyComponentHandle overridesAny) || !overridesAny.IsValid)
        {
            return;
        }

        var overridesHandle = new PrefabInstancePropertyOverridesComponentHandle(overridesAny.Index, overridesAny.Generation);
        var overrides = PrefabInstancePropertyOverridesComponent.Api.FromHandle(_propertyWorld, overridesHandle);
        if (!overrides.IsAlive || overrides.Count == 0)
        {
            return;
        }

        uint sourcePrefabStableId = 0;
        if (_world.TryGetComponent(instanceEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) && instanceAny.IsValid)
        {
            var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var instance = PrefabInstanceComponent.Api.FromHandle(_propertyWorld, instanceHandle);
            if (instance.IsAlive)
            {
                sourcePrefabStableId = instance.SourcePrefabStableId;
            }
        }

        if (sourcePrefabStableId == 0)
        {
            return;
        }

        int count = overrides.Count;
        if (count > PrefabInstancePropertyOverridesComponent.MaxOverrides)
        {
            count = PrefabInstancePropertyOverridesComponent.MaxOverrides;
        }

        ReadOnlySpan<uint> srcNode = overrides.SourceNodeStableIdReadOnlySpan().Slice(0, count);
        ReadOnlySpan<ushort> componentKind = overrides.ComponentKindReadOnlySpan().Slice(0, count);
        ReadOnlySpan<ushort> propertyIndexHint = overrides.PropertyIndexHintReadOnlySpan().Slice(0, count);
        ReadOnlySpan<ulong> propertyId = overrides.PropertyIdReadOnlySpan().Slice(0, count);
        ReadOnlySpan<ushort> propertyKind = overrides.PropertyKindReadOnlySpan().Slice(0, count);
        ReadOnlySpan<PropertyValue> value = overrides.ValueReadOnlySpan().Slice(0, count);

        Span<uint> tmpSrcNode = stackalloc uint[PrefabInstancePropertyOverridesComponent.MaxOverrides];
        Span<ushort> tmpComponentKind = stackalloc ushort[PrefabInstancePropertyOverridesComponent.MaxOverrides];
        Span<ushort> tmpPropertyIndexHint = stackalloc ushort[PrefabInstancePropertyOverridesComponent.MaxOverrides];
        Span<ulong> tmpPropertyId = stackalloc ulong[PrefabInstancePropertyOverridesComponent.MaxOverrides];
        Span<ushort> tmpPropertyKind = stackalloc ushort[PrefabInstancePropertyOverridesComponent.MaxOverrides];
        Span<PropertyValue> tmpValue = stackalloc PropertyValue[PrefabInstancePropertyOverridesComponent.MaxOverrides];

        int writeRoot = 0;
        int write = 0;

        for (int i = 0; i < count; i++)
        {
            uint sourceNodeStableId = srcNode[i];
            if (sourceNodeStableId != sourcePrefabStableId)
            {
                continue;
            }

            if (!TryApplyAndValidatePrefabOverride(
                    instanceEntity,
                    instanceRootStableId,
                    sourceNodeStableId,
                    componentKind[i],
                    propertyIndexHint[i],
                    propertyId[i],
                    propertyKind[i],
                    value[i],
                    out ushort resolvedPropertyIndexHint))
            {
                continue;
            }

            tmpSrcNode[writeRoot] = sourceNodeStableId;
            tmpComponentKind[writeRoot] = componentKind[i];
            tmpPropertyIndexHint[writeRoot] = resolvedPropertyIndexHint;
            tmpPropertyId[writeRoot] = propertyId[i];
            tmpPropertyKind[writeRoot] = propertyKind[i];
            tmpValue[writeRoot] = value[i];
            writeRoot++;
        }

        write = writeRoot;

        for (int i = 0; i < count; i++)
        {
            uint sourceNodeStableId = srcNode[i];
            if (sourceNodeStableId == 0 || sourceNodeStableId == sourcePrefabStableId)
            {
                continue;
            }

            if (!TryApplyAndValidatePrefabOverride(
                    instanceEntity,
                    instanceRootStableId,
                    sourceNodeStableId,
                    componentKind[i],
                    propertyIndexHint[i],
                    propertyId[i],
                    propertyKind[i],
                    value[i],
                    out ushort resolvedPropertyIndexHint))
            {
                continue;
            }

            tmpSrcNode[write] = sourceNodeStableId;
            tmpComponentKind[write] = componentKind[i];
            tmpPropertyIndexHint[write] = resolvedPropertyIndexHint;
            tmpPropertyId[write] = propertyId[i];
            tmpPropertyKind[write] = propertyKind[i];
            tmpValue[write] = value[i];
            write++;
        }

        Span<uint> dstSrcNode = overrides.SourceNodeStableIdSpan();
        Span<ushort> dstComponentKind = overrides.ComponentKindSpan();
        Span<ushort> dstPropertyIndexHint = overrides.PropertyIndexHintSpan();
        Span<ulong> dstPropertyId = overrides.PropertyIdSpan();
        Span<ushort> dstPropertyKind = overrides.PropertyKindSpan();
        Span<PropertyValue> dstValue = overrides.ValueSpan();

        for (int i = 0; i < write; i++)
        {
            dstSrcNode[i] = tmpSrcNode[i];
            dstComponentKind[i] = tmpComponentKind[i];
            dstPropertyIndexHint[i] = tmpPropertyIndexHint[i];
            dstPropertyId[i] = tmpPropertyId[i];
            dstPropertyKind[i] = tmpPropertyKind[i];
            dstValue[i] = tmpValue[i];
        }

        for (int i = write; i < PrefabInstancePropertyOverridesComponent.MaxOverrides; i++)
        {
            dstSrcNode[i] = 0;
            dstComponentKind[i] = 0;
            dstPropertyIndexHint[i] = 0;
            dstPropertyId[i] = 0;
            dstPropertyKind[i] = 0;
            dstValue[i] = default;
        }

        overrides.Count = (ushort)write;
        overrides.RootOverrideCount = (ushort)writeRoot;
    }

    private bool TryApplyAndValidatePrefabOverride(
        EntityId instanceEntity,
        uint instanceRootStableId,
        uint sourceNodeStableId,
        ushort componentKind,
        ushort propertyIndexHint,
        ulong propertyId,
        ushort propertyKind,
        in PropertyValue value,
        out ushort resolvedPropertyIndexHint)
    {
        resolvedPropertyIndexHint = 0;

        if (sourceNodeStableId == 0 || componentKind == 0 || propertyId == 0)
        {
            return false;
        }

        // Root proxy size is always derived from the instance canvas.
        if (componentKind == PrefabCanvasComponent.Api.PoolIdConst)
        {
            return false;
        }

        EntityId target = FindExpandedEntityBySourceStableId(instanceEntity, instanceRootStableId, sourceNodeStableId);
        if (target.IsNull)
        {
            return false;
        }

        if (!_world.TryGetComponent(target, componentKind, out AnyComponentHandle componentAny) || !componentAny.IsValid)
        {
            return false;
        }

        PropertyKind pk = (PropertyKind)propertyKind;
        if (!TryResolveBoundSlot(componentAny, propertyIndexHint, propertyId, pk, out PropertySlot slot))
        {
            return false;
        }

        WriteBoundValue(slot, pk, value);
        resolvedPropertyIndexHint = (ushort)slot.PropertyIndex;
        return true;
    }

    private EntityId FindExpandedEntityBySourceStableId(EntityId instanceEntity, uint instanceRootStableId, uint sourceNodeStableId)
    {
        if (instanceEntity.IsNull || instanceRootStableId == 0 || sourceNodeStableId == 0)
        {
            return EntityId.Null;
        }

        var nodes = new List<EntityId>(capacity: 64);
        nodes.Add(instanceEntity);
        for (int i = 0; i < nodes.Count; i++)
        {
            EntityId current = nodes[i];
            ReadOnlySpan<EntityId> children = _world.GetChildren(current);
            for (int childIndex = 0; childIndex < children.Length; childIndex++)
            {
                nodes.Add(children[childIndex]);
            }

            if (!_world.TryGetComponent(current, PrefabExpandedComponent.Api.PoolIdConst, out AnyComponentHandle expandedAny) || !expandedAny.IsValid)
            {
                continue;
            }

            var expandedHandle = new PrefabExpandedComponentHandle(expandedAny.Index, expandedAny.Generation);
            var expanded = PrefabExpandedComponent.Api.FromHandle(_propertyWorld, expandedHandle);
            if (!expanded.IsAlive || expanded.InstanceRootStableId != instanceRootStableId || expanded.SourceNodeStableId != sourceNodeStableId)
            {
                continue;
            }

            return current;
        }

        return EntityId.Null;
    }

    private EntityId EnsureExpandedRootProxy(EntityId instanceEntity, uint instanceRootStableId, EntityId sourcePrefabEntity, Vector2 instanceCanvasSize)
    {
        EntityId proxy = _world.CreateEntity(UiNodeType.PrefabInstance, instanceEntity);

        // Scaffold required components for transform + visibility + sizing.
        EnsureTransformComponent(proxy);
        EnsureBlendComponent(proxy);
        EnsureOrSetPrefabCanvasComponent(proxy, instanceCanvasSize);

        // Mark this proxy as the expanded representation of the prefab root node.
        TagAsExpanded(instanceRootStableId, sourcePrefabEntity, proxy);
        SyncExpandedRootProxy(instanceEntity, sourcePrefabEntity, proxy);
        return proxy;
    }

    private bool TryFindExpandedRootProxy(EntityId instanceEntity, uint sourcePrefabStableId, out EntityId proxyEntity)
    {
        proxyEntity = EntityId.Null;

        if (instanceEntity.IsNull || sourcePrefabStableId == 0)
        {
            return false;
        }

        uint instanceStableId = _world.GetStableId(instanceEntity);
        if (instanceStableId == 0)
        {
            return false;
        }

        ReadOnlySpan<EntityId> children = _world.GetChildren(instanceEntity);
        for (int i = 0; i < children.Length; i++)
        {
            EntityId child = children[i];
            if (!_world.TryGetComponent(child, PrefabExpandedComponent.Api.PoolIdConst, out AnyComponentHandle expandedAny) || !expandedAny.IsValid)
            {
                continue;
            }

            var expandedHandle = new PrefabExpandedComponentHandle(expandedAny.Index, expandedAny.Generation);
            var expanded = PrefabExpandedComponent.Api.FromHandle(_propertyWorld, expandedHandle);
            if (!expanded.IsAlive)
            {
                continue;
            }

            if (expanded.InstanceRootStableId == instanceStableId && expanded.SourceNodeStableId == sourcePrefabStableId)
            {
                proxyEntity = child;
                return true;
            }
        }

        return false;
    }

    private void SyncExpandedRootProxy(EntityId instanceEntity, EntityId sourcePrefabEntity, EntityId proxyEntity)
    {
        if (instanceEntity.IsNull || sourcePrefabEntity.IsNull || proxyEntity.IsNull)
        {
            return;
        }

        Vector2 instanceCanvasSize = GetPrefabInstanceCanvasSize(instanceEntity, sourcePrefabEntity);
        EnsureOrSetPrefabCanvasComponent(proxyEntity, instanceCanvasSize);
        bool sourceClipRectEnabled = true;
        if (_world.TryGetComponent(sourcePrefabEntity, ClipRectComponent.Api.PoolIdConst, out AnyComponentHandle sourceClipAny) &&
            sourceClipAny.IsValid)
        {
            var sourceClipHandle = new ClipRectComponentHandle(sourceClipAny.Index, sourceClipAny.Generation);
            var sourceClip = ClipRectComponent.Api.FromHandle(_propertyWorld, sourceClipHandle);
            if (sourceClip.IsAlive)
            {
                sourceClipRectEnabled = sourceClip.Enabled;
            }
        }

        EnsureOrSetClipRectComponent(proxyEntity, sourceClipRectEnabled);

        EnsureTransformComponent(proxyEntity);
        EnsureBlendComponent(proxyEntity);

        Vector2 instanceAnchor = Vector2.Zero;
        if (_world.TryGetComponent(instanceEntity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle instanceTransformAny) && instanceTransformAny.IsValid)
        {
            var handle = new TransformComponentHandle(instanceTransformAny.Index, instanceTransformAny.Generation);
            var view = TransformComponent.Api.FromHandle(_propertyWorld, handle);
            if (view.IsAlive)
            {
                instanceAnchor = view.Anchor;
            }
        }

        bool hasSourceTransform = false;
        Vector2 sourceAnchor = Vector2.Zero;
        Vector2 sourceScale = Vector2.One;
        float sourceRotation = 0f;
        float sourceDepth = 0f;
        bool sourceLayoutContainerEnabled = false;
        int sourceLayoutContainerLayout = 0;
        int sourceLayoutContainerDirection = 0;
        Vector4 sourceLayoutContainerPadding = default;
        float sourceLayoutContainerSpacing = 0f;
        int sourceLayoutContainerAlignItems = 0;
        int sourceLayoutContainerJustify = 0;
        int sourceLayoutContainerWidthMode = 0;
        int sourceLayoutContainerHeightMode = 0;
        int sourceLayoutContainerGridColumns = 0;
        if (_world.TryGetComponent(sourcePrefabEntity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle sourceTransformAny) && sourceTransformAny.IsValid)
        {
            var handle = new TransformComponentHandle(sourceTransformAny.Index, sourceTransformAny.Generation);
            var view = TransformComponent.Api.FromHandle(_propertyWorld, handle);
            if (view.IsAlive)
            {
                hasSourceTransform = true;
                sourceAnchor = view.Anchor;
                sourceScale = view.Scale;
                sourceRotation = view.Rotation;
                sourceDepth = view.Depth;

                sourceLayoutContainerEnabled = view.LayoutContainerEnabled;
                sourceLayoutContainerLayout = view.LayoutContainerLayout;
                sourceLayoutContainerDirection = view.LayoutContainerDirection;
                sourceLayoutContainerPadding = view.LayoutContainerPadding;
                sourceLayoutContainerSpacing = view.LayoutContainerSpacing;
                sourceLayoutContainerAlignItems = view.LayoutContainerAlignItems;
                sourceLayoutContainerJustify = view.LayoutContainerJustify;
                sourceLayoutContainerWidthMode = view.LayoutContainerWidthMode;
                sourceLayoutContainerHeightMode = view.LayoutContainerHeightMode;
                sourceLayoutContainerGridColumns = view.LayoutContainerGridColumns;
            }
        }

        bool hasSourceBlend = false;
        bool sourceBlendVisible = true;
        float sourceBlendOpacity = 1f;
        int sourceBlendMode = (int)PaintBlendMode.Normal;
        if (_world.TryGetComponent(sourcePrefabEntity, BlendComponent.Api.PoolIdConst, out AnyComponentHandle sourceBlendAny) && sourceBlendAny.IsValid)
        {
            var handle = new BlendComponentHandle(sourceBlendAny.Index, sourceBlendAny.Generation);
            var view = BlendComponent.Api.FromHandle(_propertyWorld, handle);
            if (view.IsAlive)
            {
                hasSourceBlend = true;
                sourceBlendVisible = view.IsVisible;
                sourceBlendOpacity = view.Opacity;
                sourceBlendMode = view.BlendMode;
            }
        }

        if (_world.TryGetComponent(proxyEntity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle proxyTransformAny) && proxyTransformAny.IsValid)
        {
            var proxyTransformHandle = new TransformComponentHandle(proxyTransformAny.Index, proxyTransformAny.Generation);
            var proxyTransform = TransformComponent.Api.FromHandle(_propertyWorld, proxyTransformHandle);
            if (proxyTransform.IsAlive)
            {
                if (hasSourceTransform)
                {
                    proxyTransform.Anchor = sourceAnchor;
                    proxyTransform.Scale = sourceScale;
                    proxyTransform.Rotation = sourceRotation;
                    proxyTransform.Depth = sourceDepth;

                    proxyTransform.LayoutContainerEnabled = sourceLayoutContainerEnabled;
                    proxyTransform.LayoutContainerLayout = sourceLayoutContainerLayout;
                    proxyTransform.LayoutContainerDirection = sourceLayoutContainerDirection;
                    proxyTransform.LayoutContainerPadding = sourceLayoutContainerPadding;
                    proxyTransform.LayoutContainerSpacing = sourceLayoutContainerSpacing;
                    proxyTransform.LayoutContainerAlignItems = sourceLayoutContainerAlignItems;
                    proxyTransform.LayoutContainerJustify = sourceLayoutContainerJustify;
                    proxyTransform.LayoutContainerWidthMode = sourceLayoutContainerWidthMode;
                    proxyTransform.LayoutContainerHeightMode = sourceLayoutContainerHeightMode;
                    proxyTransform.LayoutContainerGridColumns = sourceLayoutContainerGridColumns;
                }
            }
        }

        if (hasSourceBlend && _world.TryGetComponent(proxyEntity, BlendComponent.Api.PoolIdConst, out AnyComponentHandle proxyBlendAny) && proxyBlendAny.IsValid)
        {
            var proxyBlendHandle = new BlendComponentHandle(proxyBlendAny.Index, proxyBlendAny.Generation);
            var proxyBlend = BlendComponent.Api.FromHandle(_propertyWorld, proxyBlendHandle);
            if (proxyBlend.IsAlive)
            {
                proxyBlend.IsVisible = sourceBlendVisible;
                proxyBlend.Opacity = sourceBlendOpacity;
                proxyBlend.BlendMode = sourceBlendMode;
            }
        }

        ApplyPrefabRootProxyPropertyOverrides(instanceEntity, proxyEntity, sourcePrefabEntity);

        if (_world.TryGetComponent(proxyEntity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle proxyTransformAny2) && proxyTransformAny2.IsValid)
        {
            var proxyTransformHandle = new TransformComponentHandle(proxyTransformAny2.Index, proxyTransformAny2.Generation);
            var proxyTransform = TransformComponent.Api.FromHandle(_propertyWorld, proxyTransformHandle);
            if (proxyTransform.IsAlive)
            {
                Vector2 proxyAnchor = proxyTransform.Anchor;
                proxyTransform.Position = (proxyAnchor - instanceAnchor) * instanceCanvasSize;
                proxyTransform.LayoutChildIgnoreLayout = true;
                proxyTransform.LayoutConstraintEnabled = false;
            }
        }
    }

    private void ApplyPrefabRootProxyPropertyOverrides(EntityId instanceEntity, EntityId proxyEntity, EntityId sourcePrefabEntity)
    {
        if (instanceEntity.IsNull || proxyEntity.IsNull || sourcePrefabEntity.IsNull)
        {
            return;
        }

        if (!_world.TryGetComponent(instanceEntity, PrefabInstancePropertyOverridesComponent.Api.PoolIdConst, out AnyComponentHandle overridesAny) || !overridesAny.IsValid)
        {
            return;
        }

        var overridesHandle = new PrefabInstancePropertyOverridesComponentHandle(overridesAny.Index, overridesAny.Generation);
        var overrides = PrefabInstancePropertyOverridesComponent.Api.FromHandle(_propertyWorld, overridesHandle);
        if (!overrides.IsAlive || overrides.RootOverrideCount == 0)
        {
            return;
        }

        int rootCount = overrides.RootOverrideCount;
        if (rootCount > PrefabInstancePropertyOverridesComponent.MaxOverrides)
        {
            rootCount = PrefabInstancePropertyOverridesComponent.MaxOverrides;
        }

        uint sourcePrefabStableId = _world.GetStableId(sourcePrefabEntity);
        if (sourcePrefabStableId == 0)
        {
            return;
        }

        ReadOnlySpan<uint> srcNode = overrides.SourceNodeStableIdReadOnlySpan();
        ReadOnlySpan<ushort> componentKind = overrides.ComponentKindReadOnlySpan();
        ReadOnlySpan<ushort> propertyIndexHint = overrides.PropertyIndexHintReadOnlySpan();
        ReadOnlySpan<ulong> propertyId = overrides.PropertyIdReadOnlySpan();
        ReadOnlySpan<ushort> propertyKind = overrides.PropertyKindReadOnlySpan();
        ReadOnlySpan<PropertyValue> value = overrides.ValueReadOnlySpan();

        for (int i = 0; i < rootCount; i++)
        {
            if (srcNode[i] != sourcePrefabStableId)
            {
                continue;
            }

            ushort kind = componentKind[i];
            if (kind == 0)
            {
                continue;
            }

            if (kind == PrefabCanvasComponent.Api.PoolIdConst)
            {
                continue;
            }

            if (!_world.TryGetComponent(proxyEntity, kind, out AnyComponentHandle componentAny) || !componentAny.IsValid)
            {
                continue;
            }

            PropertyKind pk = (PropertyKind)propertyKind[i];
            if (!TryResolveBoundSlot(componentAny, propertyIndexHint[i], propertyId[i], pk, out PropertySlot slot))
            {
                continue;
            }

            WriteBoundValue(slot, pk, value[i]);
        }
    }

    private Vector2 GetPrefabInstanceCanvasSize(EntityId instanceEntity, EntityId sourcePrefabEntity)
    {
        if (_world.TryGetComponent(instanceEntity, PrefabCanvasComponent.Api.PoolIdConst, out AnyComponentHandle canvasAny) && canvasAny.IsValid)
        {
            var canvasHandle = new PrefabCanvasComponentHandle(canvasAny.Index, canvasAny.Generation);
            var canvas = PrefabCanvasComponent.Api.FromHandle(_propertyWorld, canvasHandle);
            if (canvas.IsAlive && canvas.Size.X > 0f && canvas.Size.Y > 0f)
            {
                return canvas.Size;
            }
        }

        if (_world.TryGetComponent(sourcePrefabEntity, PrefabCanvasComponent.Api.PoolIdConst, out AnyComponentHandle sourceCanvasAny) && sourceCanvasAny.IsValid)
        {
            var sourceCanvasHandle = new PrefabCanvasComponentHandle(sourceCanvasAny.Index, sourceCanvasAny.Generation);
            var sourceCanvas = PrefabCanvasComponent.Api.FromHandle(_propertyWorld, sourceCanvasHandle);
            if (sourceCanvas.IsAlive && sourceCanvas.Size.X > 0f && sourceCanvas.Size.Y > 0f)
            {
                return sourceCanvas.Size;
            }
        }

        return Vector2.One;
    }

    private void EnsureOrSetPrefabCanvasComponent(EntityId entity, Vector2 size)
    {
        if (size.X <= 0f || size.Y <= 0f)
        {
            size = Vector2.One;
        }

        if (_world.TryGetComponent(entity, PrefabCanvasComponent.Api.PoolIdConst, out AnyComponentHandle canvasAny) && canvasAny.IsValid)
        {
            var handle = new PrefabCanvasComponentHandle(canvasAny.Index, canvasAny.Generation);
            var view = PrefabCanvasComponent.Api.FromHandle(_propertyWorld, handle);
            if (view.IsAlive)
            {
                view.Size = size;
                return;
            }
        }

        var created = PrefabCanvasComponent.Api.Create(_propertyWorld, new PrefabCanvasComponent { Size = size });
        SetComponentWithStableId(entity, PrefabCanvasComponentProperties.ToAnyHandle(created.Handle));
    }

    private void EnsureOrSetClipRectComponent(EntityId entity, bool enabled)
    {
        if (_world.TryGetComponent(entity, ClipRectComponent.Api.PoolIdConst, out AnyComponentHandle clipAny) && clipAny.IsValid)
        {
            var clipHandle = new ClipRectComponentHandle(clipAny.Index, clipAny.Generation);
            var clip = ClipRectComponent.Api.FromHandle(_propertyWorld, clipHandle);
            if (clip.IsAlive)
            {
                clip.Enabled = enabled;
                return;
            }
        }

        var created = ClipRectComponent.Api.Create(_propertyWorld, new ClipRectComponent { Enabled = enabled });
        SetComponentWithStableId(entity, new AnyComponentHandle(ClipRectComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation));
    }

    private void ClonePrefabSubtree(EntityId sourceRoot, EntityId destParent, uint instanceRootStableId, ref byte[] scratch)
    {
        var stack = new List<CloneWorkItem>(capacity: 256);
        stack.Add(new CloneWorkItem(sourceRoot, destParent));

        while (stack.Count > 0)
        {
            int lastIndex = stack.Count - 1;
            CloneWorkItem item = stack[lastIndex];
            stack.RemoveAt(lastIndex);

            EntityId source = item.Source;
            EntityId parent = item.DestParent;

            UiNodeType nodeType = _world.GetNodeType(source);
            if (nodeType == UiNodeType.None || nodeType == UiNodeType.Prefab)
            {
                continue;
            }

            EntityId clone = _world.CreateEntity(nodeType, parent);
            CopyAllComponents(source, clone, ref scratch);
            TagAsExpanded(instanceRootStableId, source, clone);

            ReadOnlySpan<EntityId> children = _world.GetChildren(source);
            for (int i = children.Length - 1; i >= 0; i--)
            {
                stack.Add(new CloneWorkItem(children[i], clone));
            }
        }
    }

    private readonly struct CloneWorkItem
    {
        public readonly EntityId Source;
        public readonly EntityId DestParent;

        public CloneWorkItem(EntityId source, EntityId destParent)
        {
            Source = source;
            DestParent = destParent;
        }
    }

    private void CopyAllComponents(EntityId source, EntityId dest, ref byte[] scratch)
    {
        ulong presentMask = _world.GetComponentPresentMask(source);
        ReadOnlySpan<AnyComponentHandle> slots = _world.GetComponentSlots(source);

        for (int slotIndex = 0; slotIndex < UiComponentSlotMap.MaxSlots; slotIndex++)
        {
            if (((presentMask >> slotIndex) & 1UL) == 0)
            {
                continue;
            }

            AnyComponentHandle srcComponent = slots[slotIndex];
            if (!srcComponent.IsValid || srcComponent.Kind == PrefabExpandedComponent.Api.PoolIdConst)
            {
                continue;
            }

            int size = UiComponentClipboardPacker.GetSnapshotSize(srcComponent.Kind);
            if (size <= 0)
            {
                continue;
            }

            if (scratch.Length < size)
            {
                ArrayPool<byte>.Shared.Return(scratch);
                scratch = ArrayPool<byte>.Shared.Rent(size);
            }

            Span<byte> bytes = scratch.AsSpan(0, size);
            if (!UiComponentClipboardPacker.TryPack(_propertyWorld, srcComponent, bytes))
            {
                continue;
            }

            uint destStableId = _world.GetStableId(dest);
            if (!UiComponentClipboardPacker.TryCreateComponent(_propertyWorld, srcComponent.Kind, destStableId, out AnyComponentHandle dstComponent))
            {
                continue;
            }

            SetComponentWithStableId(dest, dstComponent);
            UiComponentClipboardPacker.TryUnpack(_propertyWorld, dstComponent, bytes);
        }
    }

    private void TagAsExpanded(uint instanceRootStableId, EntityId source, EntityId clone)
    {
        uint sourceStableId = _world.GetStableId(source);
        if (instanceRootStableId == 0 || sourceStableId == 0)
        {
            return;
        }

        var expanded = PrefabExpandedComponent.Api.Create(_propertyWorld, new PrefabExpandedComponent
        {
            InstanceRootStableId = instanceRootStableId,
            SourceNodeStableId = sourceStableId
        });

        SetComponentWithStableId(clone, new AnyComponentHandle(PrefabExpandedComponent.Api.PoolIdConst, expanded.Handle.Index, expanded.Handle.Generation));
    }

    private void DestroyAllChildren(EntityId parent)
    {
        ReadOnlySpan<EntityId> children = _world.GetChildren(parent);
        if (children.Length == 0)
        {
            return;
        }

        EntityId[] roots = ArrayPool<EntityId>.Shared.Rent(children.Length);
        try
        {
            for (int i = 0; i < children.Length; i++)
            {
                roots[i] = children[i];
            }

            for (int i = 0; i < children.Length; i++)
            {
                DestroyEntitySubtreeInternal(roots[i]);
            }
        }
        finally
        {
            ArrayPool<EntityId>.Shared.Return(roots);
        }
    }

    private void DestroyEntitySubtreeInternal(EntityId root)
    {
        if (root.IsNull)
        {
            return;
        }

        var nodes = new List<EntityId>(capacity: 64);
        nodes.Add(root);

        for (int i = 0; i < nodes.Count; i++)
        {
            EntityId current = nodes[i];
            ReadOnlySpan<EntityId> children = _world.GetChildren(current);
            for (int childIndex = 0; childIndex < children.Length; childIndex++)
            {
                nodes.Add(children[childIndex]);
            }
        }

        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            EntityId entity = nodes[i];
            DestroyAllComponentsOnEntity(entity);
            _world.DestroyEntity(entity);
        }
    }

    private void DestroyAllComponentsOnEntity(EntityId entity)
    {
        if (entity.IsNull)
        {
            return;
        }

        ulong presentMask = _world.GetComponentPresentMask(entity);
        ReadOnlySpan<AnyComponentHandle> slots = _world.GetComponentSlots(entity);

        for (int slotIndex = 0; slotIndex < UiComponentSlotMap.MaxSlots; slotIndex++)
        {
            if (((presentMask >> slotIndex) & 1UL) == 0)
            {
                continue;
            }

            AnyComponentHandle component = slots[slotIndex];
            if (!component.IsValid)
            {
                continue;
            }

            DestroyComponentHandle(component);
            _world.RemoveComponent(entity, component.Kind);
        }
    }

    private void DestroyComponentHandle(AnyComponentHandle component)
    {
        switch (component.Kind)
        {
            case TransformComponent.Api.PoolIdConst:
            {
                var handle = new TransformComponentHandle(component.Index, component.Generation);
                var view = TransformComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    TransformComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
            case ShapeComponent.Api.PoolIdConst:
            {
                var handle = new ShapeComponentHandle(component.Index, component.Generation);
                var view = ShapeComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    ShapeComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
            case RectGeometryComponent.Api.PoolIdConst:
            {
                var handle = new RectGeometryComponentHandle(component.Index, component.Generation);
                var view = RectGeometryComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    RectGeometryComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
            case CircleGeometryComponent.Api.PoolIdConst:
            {
                var handle = new CircleGeometryComponentHandle(component.Index, component.Generation);
                var view = CircleGeometryComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    CircleGeometryComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
            case FillComponent.Api.PoolIdConst:
            {
                var handle = new FillComponentHandle(component.Index, component.Generation);
                var view = FillComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    FillComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
            case StrokeComponent.Api.PoolIdConst:
            {
                var handle = new StrokeComponentHandle(component.Index, component.Generation);
                var view = StrokeComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    StrokeComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
            case GlowComponent.Api.PoolIdConst:
            {
                var handle = new GlowComponentHandle(component.Index, component.Generation);
                var view = GlowComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    GlowComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
            case BooleanGroupComponent.Api.PoolIdConst:
            {
                var handle = new BooleanGroupComponentHandle(component.Index, component.Generation);
                var view = BooleanGroupComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    BooleanGroupComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
            case MaskGroupComponent.Api.PoolIdConst:
            {
                var handle = new MaskGroupComponentHandle(component.Index, component.Generation);
                var view = MaskGroupComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    MaskGroupComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
            case ClipRectComponent.Api.PoolIdConst:
            {
                var handle = new ClipRectComponentHandle(component.Index, component.Generation);
                var view = ClipRectComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    ClipRectComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
            case ConstraintListComponent.Api.PoolIdConst:
            {
                var handle = new ConstraintListComponentHandle(component.Index, component.Generation);
                var view = ConstraintListComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    ConstraintListComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
            case ModifierStackComponent.Api.PoolIdConst:
            {
                var handle = new ModifierStackComponentHandle(component.Index, component.Generation);
                var view = ModifierStackComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    ModifierStackComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
            case TextComponent.Api.PoolIdConst:
            {
                var handle = new TextComponentHandle(component.Index, component.Generation);
                var view = TextComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    TextComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
            case BlendComponent.Api.PoolIdConst:
            {
                var handle = new BlendComponentHandle(component.Index, component.Generation);
                var view = BlendComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    BlendComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
            case PathComponent.Api.PoolIdConst:
            {
                var handle = new PathComponentHandle(component.Index, component.Generation);
                var view = PathComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    PathComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
            case PrefabCanvasComponent.Api.PoolIdConst:
            {
                var handle = new PrefabCanvasComponentHandle(component.Index, component.Generation);
                var view = PrefabCanvasComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    PrefabCanvasComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
            case PaintComponent.Api.PoolIdConst:
            {
                var handle = new PaintComponentHandle(component.Index, component.Generation);
                var view = PaintComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    PaintComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
            case PrefabInstanceComponent.Api.PoolIdConst:
            {
                var handle = new PrefabInstanceComponentHandle(component.Index, component.Generation);
                var view = PrefabInstanceComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    PrefabInstanceComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
            case PrefabExpandedComponent.Api.PoolIdConst:
            {
                var handle = new PrefabExpandedComponentHandle(component.Index, component.Generation);
                var view = PrefabExpandedComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    PrefabExpandedComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
            case PrefabVariablesComponent.Api.PoolIdConst:
            {
                var handle = new PrefabVariablesComponentHandle(component.Index, component.Generation);
                var view = PrefabVariablesComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    PrefabVariablesComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
            case PrefabBindingsComponent.Api.PoolIdConst:
            {
                var handle = new PrefabBindingsComponentHandle(component.Index, component.Generation);
                var view = PrefabBindingsComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    PrefabBindingsComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
            case PrefabRevisionComponent.Api.PoolIdConst:
            {
                var handle = new PrefabRevisionComponentHandle(component.Index, component.Generation);
                var view = PrefabRevisionComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    PrefabRevisionComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
            case PrefabInstanceBindingCacheComponent.Api.PoolIdConst:
            {
                var handle = new PrefabInstanceBindingCacheComponentHandle(component.Index, component.Generation);
                var view = PrefabInstanceBindingCacheComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    PrefabInstanceBindingCacheComponent.Api.Destroy(_propertyWorld, view);
                }
                return;
            }
        }
    }

    internal bool TryGetPrefabInstanceExpandedRootProxy(EntityId instanceEntity, out EntityId proxyEntity)
    {
        proxyEntity = EntityId.Null;

        if (instanceEntity.IsNull || _world.GetNodeType(instanceEntity) != UiNodeType.PrefabInstance)
        {
            return false;
        }

        if (!_world.TryGetComponent(instanceEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) || !instanceAny.IsValid)
        {
            return false;
        }

        var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
        var instance = PrefabInstanceComponent.Api.FromHandle(_propertyWorld, instanceHandle);
        if (!instance.IsAlive || instance.SourcePrefabStableId == 0)
        {
            return false;
        }

        return TryFindExpandedRootProxy(instanceEntity, instance.SourcePrefabStableId, out proxyEntity);
    }
}
