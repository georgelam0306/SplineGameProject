using System;
using System.Collections.Generic;
using System.Numerics;
using Core;
using Property.Runtime;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    private static void EnsureIndexListSize(List<int> indexById, int id)
    {
        int requiredCount = id + 1;
        if (indexById.Count >= requiredCount)
        {
            return;
        }

        while (indexById.Count < requiredCount)
        {
            indexById.Add(-1);
        }
    }

    private static void EnsureByteListSize(List<byte> listById, int id)
    {
        int requiredCount = id + 1;
        if (listById.Count >= requiredCount)
        {
            return;
        }

        while (listById.Count < requiredCount)
        {
            listById.Add(0);
        }
    }

    private static void EnsureEntityListSize(List<EntityId> entityById, int id)
    {
        int requiredCount = id + 1;
        if (entityById.Count >= requiredCount)
        {
            return;
        }

        while (entityById.Count < requiredCount)
        {
            entityById.Add(EntityId.Null);
        }
    }

    internal static void SetIndexById(List<int> indexById, int id, int index)
    {
        EnsureIndexListSize(indexById, id);
        indexById[id] = index;
    }

    internal static void SetEntityById(List<EntityId> entityById, int id, EntityId entity)
    {
        EnsureEntityListSize(entityById, id);
        entityById[id] = entity;
    }

    internal static bool TryGetEntityById(List<EntityId> entityById, int id, out EntityId entity)
    {
        entity = EntityId.Null;
        if (id <= 0 || id >= entityById.Count)
        {
            return false;
        }

        entity = entityById[id];
        return !entity.IsNull;
    }

    internal bool TryGetLegacyPrefabId(EntityId prefabEntity, out int prefabId)
    {
        prefabId = 0;
        if (prefabEntity.IsNull)
        {
            return false;
        }

        for (int i = 1; i < _prefabEntityById.Count; i++)
        {
            if (_prefabEntityById[i].Value == prefabEntity.Value)
            {
                prefabId = i;
                return true;
            }
        }

        return false;
    }

    internal bool TryGetLegacyShapeId(EntityId shapeEntity, out int shapeId)
    {
        shapeId = 0;
        if (shapeEntity.IsNull)
        {
            return false;
        }

        for (int i = 1; i < _shapeEntityById.Count; i++)
        {
            if (_shapeEntityById[i].Value == shapeEntity.Value)
            {
                shapeId = i;
                return true;
            }
        }

        return false;
    }

    internal bool TryGetLegacyGroupId(EntityId groupEntity, out int groupId)
    {
        groupId = 0;
        if (groupEntity.IsNull)
        {
            return false;
        }

        for (int i = 1; i < _groupEntityById.Count; i++)
        {
            if (_groupEntityById[i].Value == groupEntity.Value)
            {
                groupId = i;
                return true;
            }
        }

        return false;
    }

    internal bool TryGetLegacyTextId(EntityId textEntity, out int textId)
    {
        textId = 0;
        if (textEntity.IsNull)
        {
            return false;
        }

        for (int i = 1; i < _textEntityById.Count; i++)
        {
            if (_textEntityById[i].Value == textEntity.Value)
            {
                textId = i;
                return true;
            }
        }

        return false;
    }

    internal bool TryGetLegacyPrefabIndex(EntityId prefabEntity, out int prefabIndex)
    {
        prefabIndex = -1;
        if (!TryGetLegacyPrefabId(prefabEntity, out int prefabId))
        {
            return false;
        }

        return TryGetPrefabIndexById(prefabId, out prefabIndex);
    }

    internal bool TryGetShapeIndexById(int shapeId, out int shapeIndex)
    {
        if (shapeId <= 0 || shapeId >= _shapeIndexById.Count)
        {
            shapeIndex = -1;
            return false;
        }

        int idx = _shapeIndexById[shapeId];
        if (idx < 0 || idx >= _shapes.Count || _shapes[idx].Id != shapeId)
        {
            shapeIndex = -1;
            return false;
        }

        shapeIndex = idx;
        return true;
    }

    internal bool TryGetTextIndexById(int textId, out int textIndex)
    {
        if (textId <= 0 || textId >= _textIndexById.Count)
        {
            textIndex = -1;
            return false;
        }

        int idx = _textIndexById[textId];
        if (idx < 0 || idx >= _texts.Count || _texts[idx].Id != textId)
        {
            textIndex = -1;
            return false;
        }

        textIndex = idx;
        return true;
    }

    private TextLayoutCache GetOrCreateTextLayoutCache(AnyComponentHandle textAny)
    {
        int index = unchecked((int)textAny.Index);
        int generation = unchecked((int)textAny.Generation);

        while (_textLayoutByTextComponentIndex.Count <= index)
        {
            _textLayoutByTextComponentIndex.Add(null);
            _textLayoutGenerationByTextComponentIndex.Add(-1);
        }

        if (_textLayoutGenerationByTextComponentIndex[index] != generation || _textLayoutByTextComponentIndex[index] == null)
        {
            _textLayoutGenerationByTextComponentIndex[index] = generation;
            _textLayoutByTextComponentIndex[index] = new TextLayoutCache();
        }

        return _textLayoutByTextComponentIndex[index]!;
    }

    internal bool TryGetPrefabIndexById(int prefabId, out int prefabIndex)
    {
        if (prefabId <= 0 || prefabId >= _prefabIndexById.Count)
        {
            prefabIndex = -1;
            return false;
        }

        int idx = _prefabIndexById[prefabId];
        if (idx < 0 || idx >= _prefabs.Count || _prefabs[idx].Id != prefabId)
        {
            prefabIndex = -1;
            return false;
        }

        prefabIndex = idx;
        return true;
    }

    internal bool TryGetGroupIndexById(int groupId, out int groupIndex)
    {
        if (groupId <= 0 || groupId >= _groupIndexById.Count)
        {
            groupIndex = -1;
            return false;
        }

        int idx = _groupIndexById[groupId];
        if (idx < 0 || idx >= _groups.Count || _groups[idx].Id != groupId)
        {
            groupIndex = -1;
            return false;
        }

        groupIndex = idx;
        return true;
    }

}
