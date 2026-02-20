using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Property.Runtime;

namespace Derp.UI;

internal sealed class UiWorld
{
    private uint _nextStableId = 1;

    private readonly List<uint> _stableIdByEntity = new(capacity: 1024);
    private readonly List<int> _entityByStableId = new(capacity: 1024);

    private readonly List<UiHierarchyNode> _hierarchy = new(capacity: 1024);
    private readonly List<int> _childBuffer = new(capacity: 4096);

    private readonly List<ulong> _componentPresentMaskByEntity = new(capacity: 1024);
    private AnyComponentHandle[] _componentSlots = Array.Empty<AnyComponentHandle>();

    private readonly List<UiNodeType> _nodeTypeByEntity = new(capacity: 1024);

    public int EntityCount => _hierarchy.Count - 1;

    public UiWorld()
    {
        if (UiComponentSlotMap.MaxSlots > 64)
        {
            throw new InvalidOperationException("UiComponentSlotMap.MaxSlots exceeds 64; expand component presence storage beyond a single ulong mask.");
        }

        // EntityId.Null = 0 sentinel.
        _stableIdByEntity.Add(0);
        _entityByStableId.Add(-1);
        _hierarchy.Add(default);
        _componentPresentMaskByEntity.Add(0);
        _nodeTypeByEntity.Add(UiNodeType.None);
    }

    public void Clear()
    {
        _nextStableId = 1;

        _stableIdByEntity.Clear();
        _entityByStableId.Clear();
        _hierarchy.Clear();
        _childBuffer.Clear();
        _componentPresentMaskByEntity.Clear();
        _nodeTypeByEntity.Clear();
        _componentSlots = Array.Empty<AnyComponentHandle>();

        // EntityId.Null = 0 sentinel.
        _stableIdByEntity.Add(0);
        _entityByStableId.Add(-1);
        _hierarchy.Add(default);
        _componentPresentMaskByEntity.Add(0);
        _nodeTypeByEntity.Add(UiNodeType.None);
    }

    public uint GetStableId(EntityId entity)
    {
        if ((uint)entity.Value >= (uint)_stableIdByEntity.Count)
        {
            return 0;
        }

        return _stableIdByEntity[entity.Value];
    }

    public EntityId GetEntityByStableId(uint stableId)
    {
        if (stableId == 0 || stableId >= (uint)_entityByStableId.Count)
        {
            return EntityId.Null;
        }

        int entityIndex = _entityByStableId[(int)stableId];
        if (entityIndex <= 0)
        {
            return EntityId.Null;
        }

        return new EntityId(entityIndex);
    }

    public UiNodeType GetNodeType(EntityId entity)
    {
        if ((uint)entity.Value >= (uint)_nodeTypeByEntity.Count)
        {
            return UiNodeType.None;
        }

        return _nodeTypeByEntity[entity.Value];
    }

    public EntityId GetParent(EntityId entity)
    {
        if ((uint)entity.Value >= (uint)_hierarchy.Count)
        {
            return EntityId.Null;
        }

        return _hierarchy[entity.Value].Parent;
    }

    public EntityId CreateEntityWithStableId(UiNodeType nodeType, EntityId parent, uint stableId, int insertIndex)
    {
        if (stableId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stableId), "StableId must be non-zero.");
        }

        EnsureEntityByStableIdSize(stableId);
        if (_entityByStableId[(int)stableId] > 0)
        {
            throw new InvalidOperationException("StableId is already in use.");
        }

        int entityIndex = _hierarchy.Count;
        var entity = new EntityId(entityIndex);

        _stableIdByEntity.Add(stableId);
        _entityByStableId[(int)stableId] = entityIndex;
        if (stableId >= _nextStableId)
        {
            _nextStableId = stableId + 1;
        }

        _hierarchy.Add(new UiHierarchyNode
        {
            Parent = parent,
            ChildStart = -1,
            ChildCount = 0
        });

        _componentPresentMaskByEntity.Add(0);
        _nodeTypeByEntity.Add(nodeType);

        EnsureComponentSlotCapacity(entityIndex + 1);

        AddChild(parent, entity, insertIndex);
        return entity;
    }

    public EntityId CreateEntity(UiNodeType nodeType, EntityId parent)
    {
        int entityIndex = _hierarchy.Count;
        var entity = new EntityId(entityIndex);

        uint stableId = _nextStableId++;
        _stableIdByEntity.Add(stableId);
        EnsureEntityByStableIdSize(stableId);
        _entityByStableId[(int)stableId] = entityIndex;

        _hierarchy.Add(new UiHierarchyNode
        {
            Parent = parent,
            ChildStart = -1,
            ChildCount = 0
        });

        _componentPresentMaskByEntity.Add(0);
        _nodeTypeByEntity.Add(nodeType);

        EnsureComponentSlotCapacity(entityIndex + 1);

        AddChild(parent, entity, insertIndex: GetChildCount(parent));

        return entity;
    }

    public bool TryGetChildIndex(EntityId parent, EntityId child, out int index)
    {
        index = -1;
        ReadOnlySpan<EntityId> children = GetChildren(parent);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].Value == child.Value)
            {
                index = i;
                return true;
            }
        }

        return false;
    }

    public void RemoveChild(EntityId parent, EntityId child)
    {
        if ((uint)parent.Value >= (uint)_hierarchy.Count)
        {
            return;
        }

        ref UiHierarchyNode parentNode = ref CollectionsMarshal.AsSpan(_hierarchy)[parent.Value];
        if (parentNode.ChildCount <= 0 || parentNode.ChildStart < 0)
        {
            return;
        }

        int start = parentNode.ChildStart;
        int count = parentNode.ChildCount;
        for (int i = 0; i < count; i++)
        {
            if (_childBuffer[start + i] != child.Value)
            {
                continue;
            }

            for (int j = i; j < count - 1; j++)
            {
                _childBuffer[start + j] = _childBuffer[start + j + 1];
            }

            parentNode.ChildCount = count - 1;
            if (parentNode.ChildCount == 0)
            {
                parentNode.ChildStart = -1;
            }

            SetParent(child, EntityId.Null);
            return;
        }
    }

    public void MoveChild(EntityId parent, int sourceIndex, int targetIndex)
    {
        if ((uint)parent.Value >= (uint)_hierarchy.Count)
        {
            return;
        }

        ref UiHierarchyNode parentNode = ref CollectionsMarshal.AsSpan(_hierarchy)[parent.Value];
        int count = parentNode.ChildCount;
        if (count <= 1)
        {
            return;
        }

        if ((uint)sourceIndex >= (uint)count)
        {
            return;
        }

        if (targetIndex < 0)
        {
            targetIndex = 0;
        }
        else if (targetIndex >= count)
        {
            targetIndex = count - 1;
        }

        if (targetIndex == sourceIndex)
        {
            return;
        }

        int start = parentNode.ChildStart;
        int moved = _childBuffer[start + sourceIndex];

        if (targetIndex > sourceIndex)
        {
            for (int i = sourceIndex; i < targetIndex; i++)
            {
                _childBuffer[start + i] = _childBuffer[start + i + 1];
            }
        }
        else
        {
            for (int i = sourceIndex; i > targetIndex; i--)
            {
                _childBuffer[start + i] = _childBuffer[start + i - 1];
            }
        }

        _childBuffer[start + targetIndex] = moved;
    }

    public void Reparent(EntityId child, EntityId oldParent, int oldIndex, EntityId newParent, int insertIndex)
    {
        if (child.IsNull)
        {
            return;
        }

        bool sameParent = oldParent.Value == newParent.Value;
        if (sameParent && oldIndex >= 0 && insertIndex >= 0 && insertIndex > oldIndex)
        {
            insertIndex--;
        }

        if (!oldParent.IsNull)
        {
            if (oldIndex >= 0)
            {
                RemoveChildAt(oldParent, oldIndex);
            }
            else
            {
                RemoveChild(oldParent, child);
            }
        }
        else
        {
            // Root child.
            if (oldIndex >= 0)
            {
                RemoveChildAt(EntityId.Null, oldIndex);
            }
            else
            {
                RemoveChild(EntityId.Null, child);
            }
        }

        AddChild(newParent, child, insertIndex < 0 ? GetChildCount(newParent) : insertIndex);
    }

    private void RemoveChildAt(EntityId parent, int index)
    {
        if ((uint)parent.Value >= (uint)_hierarchy.Count)
        {
            return;
        }

        ref UiHierarchyNode parentNode = ref CollectionsMarshal.AsSpan(_hierarchy)[parent.Value];
        int count = parentNode.ChildCount;
        if ((uint)index >= (uint)count)
        {
            return;
        }

        int start = parentNode.ChildStart;
        int removedValue = _childBuffer[start + index];
        for (int j = index; j < count - 1; j++)
        {
            _childBuffer[start + j] = _childBuffer[start + j + 1];
        }

        parentNode.ChildCount = count - 1;
        if (parentNode.ChildCount == 0)
        {
            parentNode.ChildStart = -1;
        }

        SetParent(new EntityId(removedValue), EntityId.Null);
    }

    public ReadOnlySpan<AnyComponentHandle> GetComponentSlots(EntityId entity)
    {
        if (entity.IsNull)
        {
            return default;
        }

        int baseIndex = entity.Value * UiComponentSlotMap.MaxSlots;
        if ((uint)baseIndex >= (uint)_componentSlots.Length)
        {
            return default;
        }

        return _componentSlots.AsSpan(baseIndex, UiComponentSlotMap.MaxSlots);
    }

    public ulong GetComponentPresentMask(EntityId entity)
    {
        if ((uint)entity.Value >= (uint)_componentPresentMaskByEntity.Count)
        {
            return 0;
        }

        return _componentPresentMaskByEntity[entity.Value];
    }

    public int GetChildCount(EntityId parent)
    {
        if ((uint)parent.Value >= (uint)_hierarchy.Count)
        {
            return 0;
        }

        return _hierarchy[parent.Value].ChildCount;
    }

    public ReadOnlySpan<EntityId> GetChildren(EntityId parent)
    {
        if ((uint)parent.Value >= (uint)_hierarchy.Count)
        {
            return default;
        }

        UiHierarchyNode node = _hierarchy[parent.Value];
        if (node.ChildCount <= 0 || node.ChildStart < 0)
        {
            return default;
        }

        ReadOnlySpan<int> childrenRaw = CollectionsMarshal.AsSpan(_childBuffer).Slice(node.ChildStart, node.ChildCount);
        return MemoryMarshal.Cast<int, EntityId>(childrenRaw);
    }

    public void AddChild(EntityId parent, EntityId child, int insertIndex)
    {
        ref UiHierarchyNode parentNode = ref CollectionsMarshal.AsSpan(_hierarchy)[parent.Value];
        if (parentNode.ChildCount <= 0)
        {
            parentNode.ChildStart = _childBuffer.Count;
            parentNode.ChildCount = 1;
            _childBuffer.Add(child.Value);
            SetParent(child, parent);
            return;
        }

        int start = parentNode.ChildStart;
        int count = parentNode.ChildCount;

        insertIndex = Math.Clamp(insertIndex, 0, count);

        if (start + count == _childBuffer.Count)
        {
            // Fast path: parent owns the tail region.
            _childBuffer.Add(0);
            for (int i = count; i > insertIndex; i--)
            {
                _childBuffer[start + i] = _childBuffer[start + i - 1];
            }
            _childBuffer[start + insertIndex] = child.Value;
            parentNode.ChildCount++;
            SetParent(child, parent);
            return;
        }

        // Slow path: relocate parent's slice to the end.
        int newStart = _childBuffer.Count;
        for (int i = 0; i < count + 1; i++)
        {
            _childBuffer.Add(0);
        }

        for (int i = 0; i < insertIndex; i++)
        {
            _childBuffer[newStart + i] = _childBuffer[start + i];
        }

        _childBuffer[newStart + insertIndex] = child.Value;

        for (int i = insertIndex; i < count; i++)
        {
            _childBuffer[newStart + i + 1] = _childBuffer[start + i];
        }

        parentNode.ChildStart = newStart;
        parentNode.ChildCount = count + 1;
        SetParent(child, parent);
    }

    private void SetParent(EntityId child, EntityId parent)
    {
        ref UiHierarchyNode childNode = ref CollectionsMarshal.AsSpan(_hierarchy)[child.Value];
        childNode.Parent = parent;
    }

    private void EnsureEntityByStableIdSize(uint stableId)
    {
        int requiredCount = (int)stableId + 1;
        while (_entityByStableId.Count < requiredCount)
        {
            _entityByStableId.Add(-1);
        }
    }

    private void EnsureComponentSlotCapacity(int entityCapacity)
    {
        int requiredSlots = entityCapacity * UiComponentSlotMap.MaxSlots;
        if (_componentSlots.Length >= requiredSlots)
        {
            return;
        }

        int newSlots = Math.Max(requiredSlots, Math.Max(256, _componentSlots.Length * 2));
        Array.Resize(ref _componentSlots, newSlots);
    }

    public bool TryGetComponent(EntityId entity, ushort componentKind, out AnyComponentHandle component)
    {
        component = default;

        if ((uint)entity.Value >= (uint)_componentPresentMaskByEntity.Count)
        {
            return false;
        }

        int slotIndex = UiComponentSlotMap.GetSlotIndex(componentKind);
        if (slotIndex < 0)
        {
            return false;
        }
        ulong presentMask = _componentPresentMaskByEntity[entity.Value];
        if (((presentMask >> slotIndex) & 1UL) == 0)
        {
            return false;
        }

        int baseIndex = entity.Value * UiComponentSlotMap.MaxSlots;
        component = _componentSlots[baseIndex + slotIndex];
        return component.IsValid;
    }

    public bool HasComponent(EntityId entity, ushort componentKind)
    {
        if ((uint)entity.Value >= (uint)_componentPresentMaskByEntity.Count)
        {
            return false;
        }

        int slotIndex = UiComponentSlotMap.GetSlotIndex(componentKind);
        if (slotIndex < 0)
        {
            return false;
        }
        ulong presentMask = _componentPresentMaskByEntity[entity.Value];
        return ((presentMask >> slotIndex) & 1UL) != 0;
    }

    public void SetComponent(EntityId entity, AnyComponentHandle component)
    {
        if (component.IsNull)
        {
            return;
        }

        int slotIndex = UiComponentSlotMap.GetSlotIndex(component.Kind);
        if (slotIndex < 0)
        {
            throw new InvalidOperationException("Component kind is not registered in UiComponentSlotMap.");
        }
        EnsureComponentSlotCapacity(entity.Value + 1);

        int baseIndex = entity.Value * UiComponentSlotMap.MaxSlots;
        _componentSlots[baseIndex + slotIndex] = component;

        _componentPresentMaskByEntity[entity.Value] |= 1UL << slotIndex;
    }

    public void RemoveComponent(EntityId entity, ushort componentKind)
    {
        if ((uint)entity.Value >= (uint)_componentPresentMaskByEntity.Count)
        {
            return;
        }

        int slotIndex = UiComponentSlotMap.GetSlotIndex(componentKind);
        if (slotIndex < 0)
        {
            return;
        }
        int baseIndex = entity.Value * UiComponentSlotMap.MaxSlots;
        if ((uint)(baseIndex + slotIndex) < (uint)_componentSlots.Length)
        {
            _componentSlots[baseIndex + slotIndex] = default;
        }

        _componentPresentMaskByEntity[entity.Value] &= ~(1UL << slotIndex);
    }

    public void DestroyEntity(EntityId entity)
    {
        if (entity.IsNull)
        {
            return;
        }

        if ((uint)entity.Value >= (uint)_hierarchy.Count)
        {
            return;
        }

        EntityId parent = GetParent(entity);
        if (!parent.IsNull)
        {
            RemoveChild(parent, entity);
        }
        else
        {
            RemoveChild(EntityId.Null, entity);
        }

        uint stableId = GetStableId(entity);
        if (stableId != 0 && stableId < (uint)_entityByStableId.Count)
        {
            _entityByStableId[(int)stableId] = -1;
        }

        _stableIdByEntity[entity.Value] = 0;
        _nodeTypeByEntity[entity.Value] = UiNodeType.None;
        _hierarchy[entity.Value] = new UiHierarchyNode { Parent = EntityId.Null, ChildStart = -1, ChildCount = 0 };
        _componentPresentMaskByEntity[entity.Value] = 0;

        int baseIndex = entity.Value * UiComponentSlotMap.MaxSlots;
        int end = baseIndex + UiComponentSlotMap.MaxSlots;
        if ((uint)end <= (uint)_componentSlots.Length)
        {
            Array.Clear(_componentSlots, baseIndex, UiComponentSlotMap.MaxSlots);
        }
        else
        {
            for (int i = baseIndex; i < _componentSlots.Length; i++)
            {
                _componentSlots[i] = default;
            }
        }
    }
}
