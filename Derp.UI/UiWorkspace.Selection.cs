using DerpLib.ImGui;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    private long _inspectorInteractionCounter;
    private long _inspectorLastSelectionInteraction;
    private long _inspectorLastAnimationInteraction;

    internal void NotifyInspectorSelectionInteraction()
    {
        _inspectorInteractionCounter++;
        _inspectorLastSelectionInteraction = _inspectorInteractionCounter;
    }

    internal void NotifyInspectorAnimationInteraction()
    {
        _inspectorInteractionCounter++;
        _inspectorLastAnimationInteraction = _inspectorInteractionCounter;
    }

    internal bool PreferAnimationInspector()
    {
        return _inspectorLastAnimationInteraction > _inspectorLastSelectionInteraction;
    }

    internal bool IsEntitySelected(EntityId entity)
    {
        for (int i = 0; i < _selectedEntities.Count; i++)
        {
            if (_selectedEntities[i].Value == entity.Value)
            {
                return true;
            }
        }

        return false;
    }

    internal void SelectPrefabEntity(EntityId prefabEntity)
    {
        NotifyInspectorSelectionInteraction();
        _selectedPrefabEntity = prefabEntity;
        _selectedEntities.Clear();
        _isPrefabTransformSelectedOnCanvas = false;

        _selectedPrefabIndex = -1;
        if (!prefabEntity.IsNull && _world.GetNodeType(prefabEntity) == UiNodeType.Prefab)
        {
            if (TryGetLegacyPrefabIndex(prefabEntity, out int prefabIndex))
            {
                _selectedPrefabIndex = prefabIndex;
            }
        }
    }

    internal void SelectSingleEntity(EntityId entity)
    {
        NotifyInspectorSelectionInteraction();
        _isPrefabTransformSelectedOnCanvas = false;
        _selectedEntities.Clear();

        if (!entity.IsNull)
        {
            _selectedEntities.Add(entity);
            EntityId owningPrefab = FindOwningPrefabEntity(entity);
            _selectedPrefabEntity = owningPrefab;
            if (!owningPrefab.IsNull && TryGetLegacyPrefabIndex(owningPrefab, out int prefabIndex))
            {
                _selectedPrefabIndex = prefabIndex;
            }
        }
    }

    internal void ToggleSelectedEntity(EntityId entity)
    {
        if (entity.IsNull)
        {
            return;
        }

        NotifyInspectorSelectionInteraction();
        _isPrefabTransformSelectedOnCanvas = false;
        EntityId owningPrefab = FindOwningPrefabEntity(entity);
        if (!owningPrefab.IsNull && !_selectedPrefabEntity.IsNull && owningPrefab.Value != _selectedPrefabEntity.Value)
        {
            _selectedEntities.Clear();
        }

        _selectedPrefabEntity = owningPrefab;
        if (!owningPrefab.IsNull && TryGetLegacyPrefabIndex(owningPrefab, out int prefabIndex))
        {
            _selectedPrefabIndex = prefabIndex;
        }

        for (int i = 0; i < _selectedEntities.Count; i++)
        {
            if (_selectedEntities[i].Value == entity.Value)
            {
                _selectedEntities.RemoveAt(i);
                return;
            }
        }

        _selectedEntities.Add(entity);
    }

    internal EntityId FindOwningPrefabEntity(EntityId entity)
    {
        EntityId current = entity;
        int guard = 512;
        while (!current.IsNull && guard-- > 0)
        {
            UiNodeType type = _world.GetNodeType(current);
            if (type == UiNodeType.Prefab)
            {
                return current;
            }

            current = _world.GetParent(current);
        }

        return EntityId.Null;
    }

    internal bool SelectionIncludesGroup()
    {
        for (int i = 0; i < _selectedEntities.Count; i++)
        {
            if (_world.GetNodeType(_selectedEntities[i]) == UiNodeType.BooleanGroup)
            {
                return true;
            }
        }

        return false;
    }

    internal bool TryGetAddToGroupCandidate(out int destinationGroupId)
    {
        destinationGroupId = 0;
        if (_selectedEntities.Count < 2)
        {
            return false;
        }

        EntityId groupEntity = EntityId.Null;
        for (int i = 0; i < _selectedEntities.Count; i++)
        {
            EntityId entity = _selectedEntities[i];
            if (_world.GetNodeType(entity) != UiNodeType.BooleanGroup)
            {
                continue;
            }

            if (!groupEntity.IsNull && groupEntity.Value != entity.Value)
            {
                return false;
            }

            groupEntity = entity;
        }

        if (groupEntity.IsNull)
        {
            return false;
        }

        EntityId parentEntity = _world.GetParent(groupEntity);
        if (parentEntity.IsNull)
        {
            return false;
        }

        bool hasOtherNode = false;
        for (int i = 0; i < _selectedEntities.Count; i++)
        {
            EntityId entity = _selectedEntities[i];
            if (entity.Value == groupEntity.Value)
            {
                continue;
            }

            hasOtherNode = true;
            if (_world.GetParent(entity).Value != parentEntity.Value)
            {
                return false;
            }
        }

        if (!hasOtherNode)
        {
            return false;
        }

        if (!TryGetLegacyGroupId(groupEntity, out int groupId) || groupId <= 0)
        {
            return false;
        }

        destinationGroupId = groupId;
        return true;
    }
}
