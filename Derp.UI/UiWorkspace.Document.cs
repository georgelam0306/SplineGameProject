using System;
using System.Collections.Generic;
using System.Numerics;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using Property;
using Property.Runtime;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    private readonly UiEditorUserPreferences _userPreferences;
    internal string? DocumentPath { get; private set; }

    internal void SetDocumentPath(string? path)
    {
        DocumentPath = path;
    }

    private void SaveUserPreferences()
    {
        UiEditorUserPreferencesFile.Write(_userPreferences);
    }

    internal IReadOnlyList<string> GetRecentDocumentPaths()
    {
        return _userPreferences.RecentDocumentPaths;
    }

    internal bool TryOpenRecentDocument(string path, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Path is empty.";
            return false;
        }

        try
        {
            UiDocumentSerializer.LoadFromFile(this, path);
            SetDocumentPath(path);
            TrackRecentDocumentPath(path);
            ShowToast("Opened");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            ShowToast("Failed to open recent file: " + ex.Message);
            if (_userPreferences.RemoveRecentDocumentPath(path))
            {
                SaveUserPreferences();
            }

            return false;
        }
    }

    internal bool ClearRecentDocuments()
    {
        if (!_userPreferences.ClearRecentDocumentPaths())
        {
            return false;
        }

        SaveUserPreferences();
        return true;
    }

    internal void TrackRecentDocumentPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (_userPreferences.AddRecentDocumentPath(path))
        {
            SaveUserPreferences();
        }
    }

    internal void ResetDocumentForLoad()
    {
        Commands.ClearHistory();

        _selectedPrefabIndex = -1;
        _selectedPrefabEntity = EntityId.Null;
        _selectedEntities.Clear();

        _prefabs.Clear();
        _nextPrefabId = 1;
        _prefabIndexById.Clear();

        _shapes.Clear();
        _nextShapeId = 1;
        _shapeIndexById.Clear();

        _groups.Clear();
        _nextGroupId = 1;
        _groupIndexById.Clear();

        _texts.Clear();
        _nextTextId = 1;
        _textIndexById.Clear();
        _textLayoutByTextComponentIndex.Clear();
        _textLayoutGenerationByTextComponentIndex.Clear();

        _layersExpandedByPrefabId.Clear();
        _layersExpandedByGroupId.Clear();
        _layersExpandedByShapeId.Clear();
        _layersExpandedByTextId.Clear();

        _prefabEntityById.Clear();
        _shapeEntityById.Clear();
        _groupEntityById.Clear();
        _textEntityById.Clear();

        _prefabEntityById.Add(EntityId.Null);
        _shapeEntityById.Add(EntityId.Null);
        _groupEntityById.Add(EntityId.Null);
        _textEntityById.Add(EntityId.Null);

        _editorLayersDocument.Reset();

        _polygonPointsLocal.Clear();

        _prefabInstanceSourcePrefabStableId = 0;
        _prefabInstanceSourceStableIds = Array.Empty<uint>();

        _propertyWorld.Clear();
        _world.Clear();
        _computedLayoutEnsureNextEntityIndex = 1;

        DocumentPath = null;
    }

    internal void RebuildLegacyCachesAfterLoad(Dictionary<uint, int> prefabIdByStableId)
    {
        _prefabs.Clear();
        _prefabIndexById.Clear();
        _prefabEntityById.Clear();
        _prefabEntityById.Add(EntityId.Null);

        _nextPrefabId = 1;

        var usedPrefabIds = new HashSet<int>();

        ReadOnlySpan<EntityId> rootChildren = _world.GetChildren(EntityId.Null);
        for (int i = 0; i < rootChildren.Length; i++)
        {
            EntityId prefabEntity = rootChildren[i];
            if (_world.GetNodeType(prefabEntity) != UiNodeType.Prefab)
            {
                continue;
            }

            uint stableId = _world.GetStableId(prefabEntity);
            if (stableId == 0)
            {
                continue;
            }

            int prefabId = 0;
            if (!prefabIdByStableId.TryGetValue(stableId, out prefabId) || prefabId <= 0 || usedPrefabIds.Contains(prefabId))
            {
                prefabId = _nextPrefabId;
                while (usedPrefabIds.Contains(prefabId))
                {
                    prefabId++;
                }
            }

            usedPrefabIds.Add(prefabId);
            if (prefabId >= _nextPrefabId)
            {
                _nextPrefabId = prefabId + 1;
            }

            var prefab = new Prefab
            {
                Id = prefabId,
                RectWorld = DerivePrefabRectWorld(prefabEntity),
                Transform = GetTransformHandleOrDefault(prefabEntity),
                Animations = new AnimationDocument(),
                AnimationsCacheRevision = 0
            };

            int prefabIndex = _prefabs.Count;
            _prefabs.Add(prefab);
            SetIndexById(_prefabIndexById, prefabId, prefabIndex);
            SetEntityById(_prefabEntityById, prefabId, prefabEntity);

            uint prefabStableIdForAnimations = _world.GetStableId(prefabEntity);
            if (prefabStableIdForAnimations != 0)
            {
                if (_world.TryGetComponent(prefabEntity, AnimationLibraryComponent.Api.PoolIdConst, out AnyComponentHandle animAny) && animAny.IsValid)
                {
                    var animHandle = new AnimationLibraryComponentHandle(animAny.Index, animAny.Generation);
                    var lib = AnimationLibraryComponent.Api.FromHandle(_propertyWorld, animHandle);
                    if (lib.IsAlive)
                    {
                        Prefab prefabEntry = _prefabs[prefabIndex];
                        AnimationLibraryOps.ImportToDocument(lib, prefabEntry.Animations);
                        prefabEntry.AnimationsCacheRevision = lib.Revision;
                        _prefabs[prefabIndex] = prefabEntry;
                    }
                }
                else
                {
                    var lib = AnimationLibraryComponent.Api.CreateWithId(
                        _propertyWorld,
                        new AnimationLibraryComponentId((ulong)prefabStableIdForAnimations),
                        AnimationLibraryComponent.CreateDefault());
                    SetComponentWithStableId(prefabEntity, new AnyComponentHandle(AnimationLibraryComponent.Api.PoolIdConst, lib.Handle.Index, lib.Handle.Generation));
                    AnimationLibraryComponent.ViewProxy libView = lib;

                    Prefab prefabEntry = _prefabs[prefabIndex];
                    prefabEntry.AnimationsCacheRevision = AnimationLibraryOps.ExportFromDocument(prefabEntry.Animations, libView);
                    _prefabs[prefabIndex] = prefabEntry;
                }
            }
        }

        RebuildNodeCachesFromWorld();

        if (_selectedPrefabIndex < 0 && _prefabs.Count > 0)
        {
            _selectedPrefabIndex = 0;
            int prefabId = _prefabs[0].Id;
            TryGetEntityById(_prefabEntityById, prefabId, out _selectedPrefabEntity);
        }

        EnsureDefaultLayerNamesAfterLoad();
    }

    private void RebuildNodeCachesFromWorld()
    {
        _shapes.Clear();
        _shapeIndexById.Clear();
        _shapeEntityById.Clear();
        _shapeEntityById.Add(EntityId.Null);
        _nextShapeId = 1;

        _groups.Clear();
        _groupIndexById.Clear();
        _groupEntityById.Clear();
        _groupEntityById.Add(EntityId.Null);
        _nextGroupId = 1;

        _texts.Clear();
        _textIndexById.Clear();
        _textEntityById.Clear();
        _textEntityById.Add(EntityId.Null);
        _nextTextId = 1;

        _layersExpandedByGroupId.Clear();
        _layersExpandedByShapeId.Clear();
        _layersExpandedByTextId.Clear();

        int entityCount = _world.EntityCount;
        var shapeIdByEntityIndex = new int[entityCount + 1];
        var groupIdByEntityIndex = new int[entityCount + 1];
        var textIdByEntityIndex = new int[entityCount + 1];

        for (int prefabIndex = 0; prefabIndex < _prefabs.Count; prefabIndex++)
        {
            int prefabId = _prefabs[prefabIndex].Id;
            if (!TryGetEntityById(_prefabEntityById, prefabId, out EntityId prefabEntity) || prefabEntity.IsNull)
            {
                continue;
            }

            var stack = new List<EntityId>(capacity: 256);
            ReadOnlySpan<EntityId> prefabChildren = _world.GetChildren(prefabEntity);
            for (int i = prefabChildren.Length - 1; i >= 0; i--)
            {
                stack.Add(prefabChildren[i]);
            }

            while (stack.Count > 0)
            {
                int lastIndex = stack.Count - 1;
                EntityId entity = stack[lastIndex];
                stack.RemoveAt(lastIndex);

                UiNodeType type = _world.GetNodeType(entity);
                if (type == UiNodeType.Shape)
                {
                    int shapeId = _nextShapeId++;
                    shapeIdByEntityIndex[entity.Value] = shapeId;

                    EntityId parent = _world.GetParent(entity);
                    int parentGroupId = parent.IsNull ? 0 : groupIdByEntityIndex[parent.Value];
                    int parentShapeId = parent.IsNull ? 0 : shapeIdByEntityIndex[parent.Value];

                    var shape = new Shape
                    {
                        Id = shapeId,
                        ParentFrameId = prefabId,
                        ParentGroupId = parentGroupId,
                        ParentShapeId = parentShapeId,
                        Component = GetOrCreateShapeHandleForLoad(entity),
                        Transform = GetTransformHandleOrDefault(entity),
                        Blend = GetBlendHandleOrDefault(entity),
                        Paint = GetPaintHandleOrDefault(entity),
                        RectGeometry = GetRectGeometryHandleOrDefault(entity),
                        CircleGeometry = GetCircleGeometryHandleOrDefault(entity),
                        Path = GetPathHandleOrDefault(entity),
                        PointStart = 0,
                        PointCount = 0
                    };

                    int listIndex = _shapes.Count;
                    _shapes.Add(shape);
                    SetIndexById(_shapeIndexById, shapeId, listIndex);
                    SetEntityById(_shapeEntityById, shapeId, entity);
                }
                else if (type == UiNodeType.BooleanGroup)
                {
                    int groupId = _nextGroupId++;
                    groupIdByEntityIndex[entity.Value] = groupId;

                    EntityId parent = _world.GetParent(entity);
                    int parentGroupId = parent.IsNull ? 0 : groupIdByEntityIndex[parent.Value];
                    int parentShapeId = parent.IsNull ? 0 : shapeIdByEntityIndex[parent.Value];

                    GroupKind kind = GroupKind.Plain;
                    if (_world.HasComponent(entity, MaskGroupComponent.Api.PoolIdConst))
                    {
                        kind = GroupKind.Mask;
                    }
                    else if (_world.HasComponent(entity, BooleanGroupComponent.Api.PoolIdConst))
                    {
                        kind = GroupKind.Boolean;
                    }

                    int opIndex = 0;
                    if (_world.TryGetComponent(entity, BooleanGroupComponent.Api.PoolIdConst, out AnyComponentHandle booleanAny) && booleanAny.IsValid)
                    {
                        var handle = new BooleanGroupComponentHandle(booleanAny.Index, booleanAny.Generation);
                        var view = BooleanGroupComponent.Api.FromHandle(_propertyWorld, handle);
                        if (view.IsAlive)
                        {
                            opIndex = view.Operation;
                        }
                    }

                    var group = new BooleanGroup
                    {
                        Id = groupId,
                        ParentFrameId = prefabId,
                        ParentGroupId = parentGroupId,
                        ParentShapeId = parentShapeId,
                        Kind = kind,
                        OpIndex = opIndex,
                        Component = GetBooleanGroupHandleOrDefault(entity),
                        MaskComponent = GetMaskGroupHandleOrDefault(entity),
                        Transform = GetTransformHandleOrDefault(entity),
                        Blend = GetBlendHandleOrDefault(entity),
                    };

                    int listIndex = _groups.Count;
                    _groups.Add(group);
                    SetIndexById(_groupIndexById, groupId, listIndex);
                    SetEntityById(_groupEntityById, groupId, entity);
                }
                else if (type == UiNodeType.Text)
                {
                    int textId = _nextTextId++;
                    textIdByEntityIndex[entity.Value] = textId;

                    EntityId parent = _world.GetParent(entity);
                    int parentGroupId = parent.IsNull ? 0 : groupIdByEntityIndex[parent.Value];
                    int parentShapeId = parent.IsNull ? 0 : shapeIdByEntityIndex[parent.Value];

                    var text = new Text
                    {
                        Id = textId,
                        ParentFrameId = prefabId,
                        ParentGroupId = parentGroupId,
                        ParentShapeId = parentShapeId,
                        Component = GetTextHandleOrDefault(entity),
                        Transform = GetTransformHandleOrDefault(entity),
                        Blend = GetBlendHandleOrDefault(entity),
                        Paint = GetPaintHandleOrDefault(entity),
                        RectGeometry = GetRectGeometryHandleOrDefault(entity),
                    };

                    int listIndex = _texts.Count;
                    _texts.Add(text);
                    SetIndexById(_textIndexById, textId, listIndex);
                    SetEntityById(_textEntityById, textId, entity);
                }

                ReadOnlySpan<EntityId> children = _world.GetChildren(entity);
                for (int i = children.Length - 1; i >= 0; i--)
                {
                    stack.Add(children[i]);
                }
            }
        }
    }

    private ImRect DerivePrefabRectWorld(EntityId prefabEntity)
    {
        Vector2 position = default;
        Vector2 size = default;

        if (_world.TryGetComponent(prefabEntity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny) && transformAny.IsValid)
        {
            var handle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
            var view = TransformComponent.Api.FromHandle(_propertyWorld, handle);
            if (view.IsAlive)
            {
                position = view.Position;
            }
        }

        if (_world.TryGetComponent(prefabEntity, PrefabCanvasComponent.Api.PoolIdConst, out AnyComponentHandle canvasAny) && canvasAny.IsValid)
        {
            var handle = new PrefabCanvasComponentHandle(canvasAny.Index, canvasAny.Generation);
            var view = PrefabCanvasComponent.Api.FromHandle(_propertyWorld, handle);
            if (view.IsAlive)
            {
                size = view.Size;
            }
        }

        return new ImRect(position.X, position.Y, size.X, size.Y);
    }

    private TransformComponentHandle GetTransformHandleOrDefault(EntityId entity)
    {
        if (_world.TryGetComponent(entity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle any) && any.IsValid)
        {
            return new TransformComponentHandle(any.Index, any.Generation);
        }

        return default;
    }

    private BlendComponentHandle GetBlendHandleOrDefault(EntityId entity)
    {
        if (_world.TryGetComponent(entity, BlendComponent.Api.PoolIdConst, out AnyComponentHandle any) && any.IsValid)
        {
            return new BlendComponentHandle(any.Index, any.Generation);
        }

        return default;
    }

    private PaintComponentHandle GetPaintHandleOrDefault(EntityId entity)
    {
        if (_world.TryGetComponent(entity, PaintComponent.Api.PoolIdConst, out AnyComponentHandle any) && any.IsValid)
        {
            return new PaintComponentHandle(any.Index, any.Generation);
        }

        return default;
    }

    private ShapeComponentHandle GetOrCreateShapeHandleForLoad(EntityId entity)
    {
        ShapeKind expectedKind = ShapeKind.Rect;
        if (_world.HasComponent(entity, PathComponent.Api.PoolIdConst))
        {
            expectedKind = ShapeKind.Polygon;
        }
        else if (_world.HasComponent(entity, CircleGeometryComponent.Api.PoolIdConst))
        {
            expectedKind = ShapeKind.Circle;
        }

        if (_world.TryGetComponent(entity, ShapeComponent.Api.PoolIdConst, out AnyComponentHandle any) && any.IsValid)
        {
            var handle = new ShapeComponentHandle(any.Index, any.Generation);
            var view = ShapeComponent.Api.FromHandle(_propertyWorld, handle);
            if (view.IsAlive)
            {
                if (view.Kind != expectedKind)
                {
                    view.Kind = expectedKind;
                }

                return handle;
            }
        }

        var created = ShapeComponent.Api.Create(_propertyWorld, new ShapeComponent { Kind = expectedKind });
        SetComponentWithStableId(entity, new AnyComponentHandle(ShapeComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation));
        return created.Handle;
    }

    private TextComponentHandle GetTextHandleOrDefault(EntityId entity)
    {
        if (_world.TryGetComponent(entity, TextComponent.Api.PoolIdConst, out AnyComponentHandle any) && any.IsValid)
        {
            return new TextComponentHandle(any.Index, any.Generation);
        }

        return default;
    }

    private RectGeometryComponentHandle GetRectGeometryHandleOrDefault(EntityId entity)
    {
        if (_world.TryGetComponent(entity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle any) && any.IsValid)
        {
            return new RectGeometryComponentHandle(any.Index, any.Generation);
        }

        return default;
    }

    private CircleGeometryComponentHandle GetCircleGeometryHandleOrDefault(EntityId entity)
    {
        if (_world.TryGetComponent(entity, CircleGeometryComponent.Api.PoolIdConst, out AnyComponentHandle any) && any.IsValid)
        {
            return new CircleGeometryComponentHandle(any.Index, any.Generation);
        }
        return default;
    }

    private PathComponentHandle GetPathHandleOrDefault(EntityId entity)
    {
        if (_world.TryGetComponent(entity, PathComponent.Api.PoolIdConst, out AnyComponentHandle any) && any.IsValid)
        {
            return new PathComponentHandle(any.Index, any.Generation);
        }

        return default;
    }

    private BooleanGroupComponentHandle GetBooleanGroupHandleOrDefault(EntityId entity)
    {
        if (_world.TryGetComponent(entity, BooleanGroupComponent.Api.PoolIdConst, out AnyComponentHandle any) && any.IsValid)
        {
            return new BooleanGroupComponentHandle(any.Index, any.Generation);
        }

        return default;
    }

    private MaskGroupComponentHandle GetMaskGroupHandleOrDefault(EntityId entity)
    {
        if (_world.TryGetComponent(entity, MaskGroupComponent.Api.PoolIdConst, out AnyComponentHandle any) && any.IsValid)
        {
            return new MaskGroupComponentHandle(any.Index, any.Generation);
        }

        return default;
    }
}
