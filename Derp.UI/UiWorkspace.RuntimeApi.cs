using System;
using System.Numerics;
using Core;
using Property;
using Property.Runtime;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    internal bool TrySetCanvasSizeForEntityRuntime(EntityId entity, Vector2 size)
    {
        if (entity.IsNull)
        {
            return false;
        }

        if (!_world.TryGetComponent(entity, PrefabCanvasComponent.Api.PoolIdConst, out AnyComponentHandle canvasAny) || !canvasAny.IsValid)
        {
            return false;
        }

        var canvasHandle = new PrefabCanvasComponentHandle(canvasAny.Index, canvasAny.Generation);
        var canvas = PrefabCanvasComponent.Api.FromHandle(_propertyWorld, canvasHandle);
        if (!canvas.IsAlive)
        {
            return false;
        }

        canvas.Size = size;
        _ = TrySetComputedSize(entity, size);
        return true;
    }

    internal bool TrySetPrefabInstanceCanvasSizeRuntime(EntityId instanceEntity, Vector2 size, bool markOverridden)
    {
        if (instanceEntity.IsNull || _world.GetNodeType(instanceEntity) != UiNodeType.PrefabInstance)
        {
            return false;
        }

        if (markOverridden &&
            _world.TryGetComponent(instanceEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) &&
            instanceAny.IsValid)
        {
            var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var instance = PrefabInstanceComponent.Api.FromHandle(_propertyWorld, instanceHandle);
            if (instance.IsAlive)
            {
                instance.CanvasSizeIsOverridden = 1;
            }
        }

        TrySetPrefabCanvasSizeFromLayout(instanceEntity, size);
        _ = TrySetComputedSize(instanceEntity, size);
        SyncExpandedRootProxySizeFromInstance(instanceEntity, size);
        return true;
    }

    internal bool TrySetPrefabInstanceVariableOverrideRuntime(EntityId instanceEntity, ushort variableId, in PropertyValue value)
    {
        if (instanceEntity.IsNull)
        {
            return false;
        }

        UiNodeType nodeType = _world.GetNodeType(instanceEntity);
        if (nodeType == UiNodeType.Prefab)
        {
            EnsureRuntimePrefabVariableStore(instanceEntity);
        }

        if (!_world.TryGetComponent(instanceEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) || !instanceAny.IsValid)
        {
            return false;
        }

        var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
        var instance = PrefabInstanceComponent.Api.FromHandle(_propertyWorld, instanceHandle);
        if (!instance.IsAlive)
        {
            return false;
        }

        ushort count = instance.ValueCount;
        int n = Math.Min((int)count, PrefabInstanceComponent.MaxVariables);

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
                return false;
            }

            index = n;
            ids[index] = variableId;
            instance.ValueCount = (ushort)(n + 1);
        }

        values[index] = value;
        instance.OverrideMask = overrideMask | (1UL << index);
        return true;
    }

    internal bool TryResolvePrefabVariableId(uint prefabStableId, ReadOnlySpan<char> name, out ushort variableId, out PropertyKind kind)
    {
        variableId = 0;
        kind = default;

        if (prefabStableId == 0 || name.Length == 0)
        {
            return false;
        }

        EntityId prefabEntity = _world.GetEntityByStableId(prefabStableId);
        if (prefabEntity.IsNull || _world.GetNodeType(prefabEntity) != UiNodeType.Prefab)
        {
            return false;
        }

        if (!_world.TryGetComponent(prefabEntity, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) || !varsAny.IsValid)
        {
            return false;
        }

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        var vars = PrefabVariablesComponent.Api.FromHandle(_propertyWorld, varsHandle);
        if (!vars.IsAlive)
        {
            return false;
        }

        ushort count = vars.VariableCount;
        int n = Math.Min((int)count, PrefabVariablesComponent.MaxVariables);

        ReadOnlySpan<StringHandle> names = vars.NameReadOnlySpan();
        ReadOnlySpan<ushort> ids = vars.VariableIdReadOnlySpan();
        ReadOnlySpan<int> kinds = vars.KindReadOnlySpan();

        for (int i = 0; i < n; i++)
        {
            string varName = (string)names[i];
            if (!name.SequenceEqual(varName.AsSpan()))
            {
                continue;
            }

            variableId = ids[i];
            kind = (PropertyKind)kinds[i];
            return variableId != 0;
        }

        return false;
    }
}
