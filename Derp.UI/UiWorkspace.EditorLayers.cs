using System;
using System.Collections.Generic;
using Property.Runtime;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    internal void EnsureDefaultLayerNamesAfterLoad()
    {
        int entityCount = _world.EntityCount;
        for (int entityIndex = 1; entityIndex <= entityCount; entityIndex++)
        {
            var entity = new EntityId(entityIndex);
            if (_world.GetNodeType(entity) == UiNodeType.None)
            {
                continue;
            }

            EnsureDefaultLayerName(entity);
        }
    }

    internal void EnsureDefaultLayerName(EntityId entity)
    {
        if (entity.IsNull)
        {
            return;
        }

        uint stableId = _world.GetStableId(entity);
        if (stableId == 0)
        {
            return;
        }

        if (TryGetLayerName(stableId, out _))
        {
            return;
        }

        string name = BuildDefaultLayerName(entity, stableId);
        if (!string.IsNullOrEmpty(name))
        {
            SetLayerName(stableId, name);
        }
    }

    internal bool IsEntityHiddenInEditor(EntityId entity)
    {
        EntityId current = entity;
        int guard = 512;
        while (!current.IsNull && guard-- > 0)
        {
            uint stableId = _world.GetStableId(current);
            if (GetLayerHidden(stableId))
            {
                return true;
            }
            current = _world.GetParent(current);
        }

        return false;
    }

    internal bool IsEntityLockedInEditor(EntityId entity)
    {
        EntityId current = entity;
        int guard = 512;
        while (!current.IsNull && guard-- > 0)
        {
            if (_world.HasComponent(current, ListGeneratedComponent.Api.PoolIdConst))
            {
                return true;
            }

            uint stableId = _world.GetStableId(current);
            if (GetLayerLocked(stableId))
            {
                return true;
            }
            current = _world.GetParent(current);
        }

        return false;
    }

    internal bool GetLayerHidden(uint stableId)
    {
        return GetStableIdFlag(_editorLayersDocument.HiddenByStableId, stableId);
    }

    internal bool GetLayerLocked(uint stableId)
    {
        return GetStableIdFlag(_editorLayersDocument.LockedByStableId, stableId);
    }

    internal void SetLayerHidden(uint stableId, bool hidden)
    {
        SetStableIdFlag(_editorLayersDocument.HiddenByStableId, stableId, hidden);
    }

    internal void SetLayerLocked(uint stableId, bool locked)
    {
        SetStableIdFlag(_editorLayersDocument.LockedByStableId, stableId, locked);
    }

    internal bool TryGetLayerName(uint stableId, out string name)
    {
        name = string.Empty;
        if (stableId == 0)
        {
            return false;
        }

        EnsureStableIdListSize(_editorLayersDocument.NameByStableId, stableId, string.Empty);
        name = _editorLayersDocument.NameByStableId[(int)stableId];
        return !string.IsNullOrEmpty(name);
    }

    internal void SetLayerName(uint stableId, string name)
    {
        if (stableId == 0)
        {
            return;
        }

        EnsureStableIdListSize(_editorLayersDocument.NameByStableId, stableId, string.Empty);
        _editorLayersDocument.NameByStableId[(int)stableId] = name ?? string.Empty;
    }

    internal bool GetLayerExpanded(uint stableId)
    {
        return GetStableIdFlag(_editorLayersDocument.ExpandedByStableId, stableId);
    }

    internal void SetLayerExpanded(uint stableId, bool expanded)
    {
        SetStableIdFlag(_editorLayersDocument.ExpandedByStableId, stableId, expanded);
    }

    private static bool GetStableIdFlag(List<byte> flagsByStableId, uint stableId)
    {
        if (stableId == 0)
        {
            return false;
        }

        EnsureStableIdListSize(flagsByStableId, stableId, defaultValue: (byte)0);
        return flagsByStableId[(int)stableId] != 0;
    }

    private static void SetStableIdFlag(List<byte> flagsByStableId, uint stableId, bool value)
    {
        if (stableId == 0)
        {
            return;
        }

        EnsureStableIdListSize(flagsByStableId, stableId, defaultValue: (byte)0);
        flagsByStableId[(int)stableId] = value ? (byte)1 : (byte)0;
    }

    private static void EnsureStableIdListSize<T>(List<T> list, uint stableId, T defaultValue)
    {
        int index = (int)stableId;
        while (list.Count <= index)
        {
            list.Add(defaultValue);
        }
    }

    private string BuildDefaultLayerName(EntityId entity, uint stableId)
    {
        UiNodeType type = _world.GetNodeType(entity);
        if (type == UiNodeType.Prefab)
        {
            if (TryGetLegacyPrefabId(entity, out int prefabId) && prefabId > 0)
            {
                return "Prefab " + prefabId;
            }

            return "Prefab " + stableId;
        }

        if (type == UiNodeType.Shape)
        {
            ShapeKind kind = ShapeKind.Rect;
            if (_world.TryGetComponent(entity, ShapeComponent.Api.PoolIdConst, out AnyComponentHandle shapeAny) && shapeAny.IsValid)
            {
                var handle = new ShapeComponentHandle(shapeAny.Index, shapeAny.Generation);
                var view = ShapeComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    kind = view.Kind;
                }
            }
            else if (TryGetLegacyShapeId(entity, out int shapeId) && TryGetShapeIndexById(shapeId, out int shapeIndex))
            {
                ShapeComponentHandle handle = _shapes[shapeIndex].Component;
                if (!handle.IsNull)
                {
                    var view = ShapeComponent.Api.FromHandle(_propertyWorld, handle);
                    if (view.IsAlive)
                    {
                        kind = view.Kind;
                    }
                }
            }
            else if (_world.HasComponent(entity, PathComponent.Api.PoolIdConst))
            {
                kind = ShapeKind.Polygon;
            }
            else if (_world.HasComponent(entity, CircleGeometryComponent.Api.PoolIdConst))
            {
                kind = ShapeKind.Circle;
            }

            string kindLabel = kind switch
            {
                ShapeKind.Rect => "Rectangle",
                ShapeKind.Circle => "Ellipse",
                ShapeKind.Polygon => "Polygon",
                _ => "Shape"
            };

            return kindLabel + " " + stableId;
        }

        if (type == UiNodeType.BooleanGroup)
        {
            if (_world.HasComponent(entity, MaskGroupComponent.Api.PoolIdConst))
            {
                return "Mask " + stableId;
            }

            if (_world.HasComponent(entity, BooleanGroupComponent.Api.PoolIdConst))
            {
                return "Boolean Group " + stableId;
            }

            return "Group " + stableId;
        }

        if (type == UiNodeType.Text)
        {
            return "Text " + stableId;
        }

        if (type == UiNodeType.PrefabInstance)
        {
            return "Instance " + stableId;
        }

        return "Node " + stableId;
    }
}
