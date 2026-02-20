using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Core;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using Property;
using Property.Runtime;

namespace Derp.UI;

internal sealed class WorkspaceCommands
{
    private readonly UiWorkspace _workspace;
    private readonly UndoStack _undoStack = new();
    private readonly List<EntityMoveUndoPayload> _undoEntityMovePayload = new(capacity: 256);
    private readonly List<PropertyUndoPayload> _undoPropertyPayload = new(capacity: 512);
    private readonly List<ComponentSnapshotUndoPayload> _undoComponentSnapshotPayload = new(capacity: 128);

    private struct EntityMoveUndoPayload
    {
        public uint EntityStableId;
        public int SourceIndex;
    }

    private struct PropertyUndoPayload
    {
        public PropertyKey Key;
        public PropertyValue Before;
        public PropertyValue After;
    }

    private struct ComponentSnapshotUndoPayload
    {
        public uint EntityStableId;
        public ushort ComponentKind;
        public byte[] Before;
        public byte[] After;
    }

    private readonly struct ComponentSnapshotTarget
    {
        public readonly uint EntityStableId;
        public readonly AnyComponentHandle Component;

        public ComponentSnapshotTarget(uint entityStableId, AnyComponentHandle component)
        {
            EntityStableId = entityStableId;
            Component = component;
        }
    }

    private struct PendingEdit
    {
        public int WidgetId;
        public UndoRecord PendingRecord;
        public bool IsActive;
    }

    private PendingEdit _pendingEdit;
    private ClipboardDocument? _clipboard;

    private EntityId _activeInspectorEntity;

    internal void SetActiveInspectorEntity(EntityId entity)
    {
        _activeInspectorEntity = entity;
    }

    private sealed class ClipboardDocument
    {
        public readonly List<ClipboardNode> Nodes = new(capacity: 128);
        public readonly List<ClipboardComponent> Components = new(capacity: 256);
        public readonly List<int> RootNodeIndices = new(capacity: 16);
    }

    private struct ClipboardNode
    {
        public int ParentIndex;
        public UiNodeType NodeType;

        public bool EditorHidden;
        public bool EditorLocked;
        public string EditorName;

        public UiWorkspace.GroupKind GroupKind;
        public int GroupOpIndex;

        public int ComponentStart;
        public int ComponentCount;
    }

    private struct ClipboardComponent
    {
        public ushort Kind;
        public byte[] Data;
    }

    private static readonly StringHandle FillGroupName = "Fill";
    private static readonly StringHandle GradientGroupName = "Gradient";
    private static readonly StringHandle StopsGroupName = "Stops";

    private static readonly StringHandle FillColorName = "Color";
    private static readonly StringHandle FillUseGradientName = "Use Gradient";

    private static readonly StringHandle GradientColorAName = "Color A";
    private static readonly StringHandle GradientColorBName = "Color B";
    private static readonly StringHandle GradientMixName = "Mix";
    private static readonly StringHandle GradientDirectionName = "Direction";

    private static readonly StringHandle StopsT0To3Name = "T 0-3";
    private static readonly StringHandle StopsT4To7Name = "T 4-7";
    private static readonly StringHandle StopsColor0Name = "Color 0";
    private static readonly StringHandle StopsColor1Name = "Color 1";
    private static readonly StringHandle StopsColor2Name = "Color 2";
    private static readonly StringHandle StopsColor3Name = "Color 3";
    private static readonly StringHandle StopsColor4Name = "Color 4";
    private static readonly StringHandle StopsColor5Name = "Color 5";
    private static readonly StringHandle StopsColor6Name = "Color 6";
    private static readonly StringHandle StopsColor7Name = "Color 7";

    public void SetTransformAnchorMaintainParentSpace(int widgetId, bool isEditing, EntityId entity, PropertySlot positionXSlot, PropertySlot positionYSlot, PropertySlot anchorXSlot, PropertySlot anchorYSlot, Vector2 newAnchor)
    {
        if (entity.IsNull)
        {
            SetPropertyBatch2(widgetId, isEditing, anchorXSlot, PropertyValue.FromFloat(newAnchor.X), anchorYSlot, PropertyValue.FromFloat(newAnchor.Y));
            return;
        }

        if (!TryResolveSlot(PropertyKey.FromSlot(positionXSlot), out PropertySlot posX) ||
            !TryResolveSlot(PropertyKey.FromSlot(positionYSlot), out PropertySlot posY) ||
            !TryResolveSlot(PropertyKey.FromSlot(anchorXSlot), out PropertySlot anchorX) ||
            !TryResolveSlot(PropertyKey.FromSlot(anchorYSlot), out PropertySlot anchorY))
        {
            return;
        }

        if (!_workspace.World.TryGetComponent(entity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny))
        {
            SetPropertyBatch2(widgetId, isEditing, anchorXSlot, PropertyValue.FromFloat(newAnchor.X), anchorYSlot, PropertyValue.FromFloat(newAnchor.Y));
            return;
        }

        var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
        var transform = TransformComponent.Api.FromHandle(_workspace.PropertyWorld, transformHandle);
        if (!transform.IsAlive)
        {
            SetPropertyBatch2(widgetId, isEditing, anchorXSlot, PropertyValue.FromFloat(newAnchor.X), anchorYSlot, PropertyValue.FromFloat(newAnchor.Y));
            return;
        }

        if (!TryGetAnchorBoundsSizeLocal(entity, out Vector2 boundsSizeLocal))
        {
            SetPropertyBatch2(widgetId, isEditing, anchorXSlot, PropertyValue.FromFloat(newAnchor.X), anchorYSlot, PropertyValue.FromFloat(newAnchor.Y));
            return;
        }

        Vector2 oldAnchor = transform.Anchor;
        Vector2 position = transform.Position;

        Vector2 localScale = transform.Scale;
        float scaleX = localScale.X == 0f ? 1f : localScale.X;
        float scaleY = localScale.Y == 0f ? 1f : localScale.Y;
        float width = boundsSizeLocal.X * MathF.Abs(scaleX);
        float height = boundsSizeLocal.Y * MathF.Abs(scaleY);

        float rotationRadians = transform.Rotation * (MathF.PI / 180f);

        Vector2 oldAnchorOffset = new Vector2((oldAnchor.X - 0.5f) * width, (oldAnchor.Y - 0.5f) * height);
        Vector2 center = position - RotateVector(oldAnchorOffset, rotationRadians);

        Vector2 newAnchorOffset = new Vector2((newAnchor.X - 0.5f) * width, (newAnchor.Y - 0.5f) * height);
        Vector2 newPosition = center + RotateVector(newAnchorOffset, rotationRadians);

        SetPropertyBatch4(
            widgetId,
            isEditing,
            posX,
            PropertyValue.FromFloat(newPosition.X),
            posY,
            PropertyValue.FromFloat(newPosition.Y),
            anchorX,
            PropertyValue.FromFloat(newAnchor.X),
            anchorY,
            PropertyValue.FromFloat(newAnchor.Y));
    }

    public void SetTransformAnchorMaintainParentSpaceVec2(int widgetId, bool isEditing, EntityId entity, PropertySlot positionSlot, PropertySlot anchorSlot, Vector2 newAnchor)
    {
        if (entity.IsNull)
        {
            SetPropertyValue(widgetId, isEditing, anchorSlot, PropertyValue.FromVec2(newAnchor));
            return;
        }

        if (!TryResolveSlot(PropertyKey.FromSlot(positionSlot), out PropertySlot positionResolved) ||
            !TryResolveSlot(PropertyKey.FromSlot(anchorSlot), out PropertySlot anchorResolved))
        {
            return;
        }

        if (!_workspace.World.TryGetComponent(entity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny))
        {
            SetPropertyValue(widgetId, isEditing, anchorResolved, PropertyValue.FromVec2(newAnchor));
            return;
        }

        var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
        var transform = TransformComponent.Api.FromHandle(_workspace.PropertyWorld, transformHandle);
        if (!transform.IsAlive)
        {
            SetPropertyValue(widgetId, isEditing, anchorResolved, PropertyValue.FromVec2(newAnchor));
            return;
        }

        if (!TryGetAnchorBoundsSizeLocal(entity, out Vector2 boundsSizeLocal))
        {
            SetPropertyValue(widgetId, isEditing, anchorResolved, PropertyValue.FromVec2(newAnchor));
            return;
        }

        Vector2 oldAnchor = transform.Anchor;
        Vector2 position = transform.Position;

        Vector2 localScale = transform.Scale;
        float scaleX = localScale.X == 0f ? 1f : localScale.X;
        float scaleY = localScale.Y == 0f ? 1f : localScale.Y;
        float width = boundsSizeLocal.X * MathF.Abs(scaleX);
        float height = boundsSizeLocal.Y * MathF.Abs(scaleY);

        float rotationRadians = transform.Rotation * (MathF.PI / 180f);

        Vector2 oldAnchorOffset = new Vector2((oldAnchor.X - 0.5f) * width, (oldAnchor.Y - 0.5f) * height);
        Vector2 center = position - RotateVector(oldAnchorOffset, rotationRadians);

        Vector2 newAnchorOffset = new Vector2((newAnchor.X - 0.5f) * width, (newAnchor.Y - 0.5f) * height);
        Vector2 newPosition = center + RotateVector(newAnchorOffset, rotationRadians);

        SetPropertyBatch2(
            widgetId,
            isEditing,
            positionResolved,
            PropertyValue.FromVec2(newPosition),
            anchorResolved,
            PropertyValue.FromVec2(newAnchor));
    }

    public WorkspaceCommands(UiWorkspace workspace)
    {
        _workspace = workspace;
    }

    internal bool TryGetSelectionTarget(out EntityId entity)
    {
        if (_workspace.TryGetAnimationTargetOverride(out entity))
        {
            return true;
        }

        if (_workspace._selectedEntities.Count > 0)
        {
            entity = _workspace._selectedEntities[0];
            return !entity.IsNull;
        }

        if (!_workspace._selectedPrefabEntity.IsNull)
        {
            entity = _workspace._selectedPrefabEntity;
            return true;
        }

        entity = EntityId.Null;
        return false;
    }

    internal void SetAnimationTargetOverride(EntityId entity)
    {
        _workspace.SetAnimationTargetOverride(entity);
    }

    public bool CanUndo => _undoStack.UndoCount > 0;
    public bool CanRedo => _undoStack.RedoCount > 0;

    public void ClearHistory()
    {
        DiscardPendingEdits();
        _undoStack.Clear();
        _undoEntityMovePayload.Clear();
        _undoPropertyPayload.Clear();
        _undoComponentSnapshotPayload.Clear();
    }

    public void FlushPendingEdits()
    {
        if (!_pendingEdit.IsActive)
        {
            return;
        }

        CommitPendingEdit();
    }

    public void DiscardPendingEdits()
    {
        _pendingEdit = default;
    }

    public void Undo()
    {
        FlushPendingEdits();

        if (!_undoStack.TryPopUndo(out UndoRecord record))
        {
            return;
        }

        ApplyUndoRecord(record, applyAfterValues: false);
        _undoStack.PushRedo(record);
    }

    public void Redo()
    {
        FlushPendingEdits();

        if (!_undoStack.TryPopRedo(out UndoRecord record))
        {
            return;
        }

        ApplyUndoRecord(record, applyAfterValues: true);
        _undoStack.PushUndo(record);
    }

    public bool CopySelectionToClipboard()
    {
        FlushPendingEdits();

        List<EntityId> selected = _workspace._selectedEntities;
        if (selected.Count == 0)
        {
            return false;
        }

        var clipboard = new ClipboardDocument();

        for (int i = 0; i < selected.Count; i++)
        {
            EntityId entity = selected[i];
            if (entity.IsNull)
            {
                continue;
            }

            if (HasSelectedAncestor(selected, entity))
            {
                continue;
            }

            int nodeIndex = CopySubtreeToClipboard(entity, parentIndex: -1, clipboard);
            if (nodeIndex >= 0)
            {
                clipboard.RootNodeIndices.Add(nodeIndex);
            }
        }

        if (clipboard.RootNodeIndices.Count == 0)
        {
            return false;
        }

        _clipboard = clipboard;
        return true;
    }

    public bool PasteClipboardAtCursor()
    {
        FlushPendingEdits();

        ClipboardDocument? clipboard = _clipboard;
        if (clipboard == null || clipboard.Nodes.Count == 0)
        {
            return false;
        }

        if (!TryGetPasteParentEntity(out EntityId pasteParent))
        {
            return false;
        }

        if (!TryRestoreClipboard(clipboard, pasteParent, insertIndex: -1, selectPasted: true, placeAtCursor: true, pastedRootsOut: null))
        {
            return false;
        }

        return true;
    }

    public bool DuplicateSelectionAsSiblings()
    {
        FlushPendingEdits();

        List<EntityId> selected = _workspace._selectedEntities;
        if (selected.Count == 0)
        {
            return false;
        }

        Span<EntityId> roots = stackalloc EntityId[32];
        Span<EntityId> rootParents = stackalloc EntityId[32];
        Span<int> rootParentIndices = stackalloc int[32];
        int rootCount = 0;

        for (int i = 0; i < selected.Count; i++)
        {
            EntityId entity = selected[i];
            if (entity.IsNull)
            {
                continue;
            }

            if (HasSelectedAncestor(selected, entity))
            {
                continue;
            }

            EntityId parent = _workspace.World.GetParent(entity);
            if (parent.IsNull)
            {
                continue;
            }

            if (!_workspace.World.TryGetChildIndex(parent, entity, out int parentIndex))
            {
                parentIndex = 0;
            }

            if (rootCount >= roots.Length)
            {
                break;
            }

            roots[rootCount] = entity;
            rootParents[rootCount] = parent;
            rootParentIndices[rootCount] = parentIndex;
            rootCount++;
        }

        if (rootCount == 0)
        {
            return false;
        }

        // Sort roots by (parent entity, index) so we can duplicate as sibling blocks.
        for (int i = 1; i < rootCount; i++)
        {
            EntityId entity = roots[i];
            EntityId parent = rootParents[i];
            int parentIndex = rootParentIndices[i];

            int j = i - 1;
            while (j >= 0)
            {
                bool parentAfter = rootParents[j].Value > parent.Value;
                bool sameParentIndexAfter = rootParents[j].Value == parent.Value && rootParentIndices[j] > parentIndex;

                if (!parentAfter && !sameParentIndexAfter)
                {
                    break;
                }

                roots[j + 1] = roots[j];
                rootParents[j + 1] = rootParents[j];
                rootParentIndices[j + 1] = rootParentIndices[j];
                j--;
            }

            roots[j + 1] = entity;
            rootParents[j + 1] = parent;
            rootParentIndices[j + 1] = parentIndex;
        }

        bool duplicatedAny = false;
        var duplicatedRoots = new List<EntityId>(capacity: rootCount);

        int start = 0;
        while (start < rootCount)
        {
            EntityId parent = rootParents[start];
            if (parent.IsNull)
            {
                start++;
                continue;
            }

            int end = start + 1;
            int maxIndex = rootParentIndices[start];
            while (end < rootCount && rootParents[end].Value == parent.Value)
            {
                maxIndex = Math.Max(maxIndex, rootParentIndices[end]);
                end++;
            }

            Span<EntityId> groupRoots = roots.Slice(start, end - start);

            var tempClipboard = new ClipboardDocument();
            for (int i = 0; i < groupRoots.Length; i++)
            {
                int nodeIndex = CopySubtreeToClipboard(groupRoots[i], parentIndex: -1, tempClipboard);
                if (nodeIndex >= 0)
                {
                    tempClipboard.RootNodeIndices.Add(nodeIndex);
                }
            }

            if (tempClipboard.RootNodeIndices.Count > 0)
            {
                int insertIndex = maxIndex + 1;
                if (TryRestoreClipboard(tempClipboard, parent, insertIndex, selectPasted: false, placeAtCursor: false, pastedRootsOut: duplicatedRoots))
                {
                    duplicatedAny = true;
                }
            }

            start = end;
        }

        if (duplicatedAny && duplicatedRoots.Count > 0)
        {
            _workspace.SelectSingleEntity(duplicatedRoots[0]);
            for (int i = 1; i < duplicatedRoots.Count; i++)
            {
                _workspace.ToggleSelectedEntity(duplicatedRoots[i]);
            }
        }

        return duplicatedAny;
    }

	    public bool DeleteSelectedLayers()
	    {
	        FlushPendingEdits();

	        List<EntityId> selected = _workspace._selectedEntities;
	        if (selected.Count == 0)
	        {
	            return DeleteSelectedPrefab();
	        }

	        bool deletedAny = false;

	        for (int i = 0; i < selected.Count; i++)
	        {
	            EntityId entity = _workspace.ResolvePrefabInstanceRootEntity(selected[i]);
	            if (entity.IsNull)
	            {
	                continue;
	            }

	            if (_workspace.IsEntityLockedInEditor(entity))
	            {
	                continue;
	            }

	            if (HasSelectedAncestor(selected, entity))
	            {
	                continue;
	            }

	            if (DeleteSingleLayer(entity))
	            {
	                deletedAny = true;
	            }
	        }

        if (deletedAny)
        {
            _workspace.BumpSelectedPrefabRevision();
            _workspace.ClearSelection();
        }

        return deletedAny;
    }

    private bool DeleteSelectedPrefab()
    {
        EntityId prefabEntity = _workspace._selectedPrefabEntity;
        if (prefabEntity.IsNull || _workspace.World.GetNodeType(prefabEntity) != UiNodeType.Prefab)
        {
            return false;
        }

        if (_workspace._selectedPrefabIndex < 0 || _workspace._selectedPrefabIndex >= _workspace._prefabs.Count)
        {
            if (_workspace.TryGetLegacyPrefabIndex(prefabEntity, out int prefabIndex))
            {
                _workspace._selectedPrefabIndex = prefabIndex;
            }
        }

        int selectedPrefabIndex = _workspace._selectedPrefabIndex;
        if (selectedPrefabIndex < 0 || selectedPrefabIndex >= _workspace._prefabs.Count)
        {
            return false;
        }

        UiWorkspace.Prefab prefab = _workspace._prefabs[selectedPrefabIndex];

        uint prefabStableId = _workspace.World.GetStableId(prefabEntity);
        if (prefabStableId == 0)
        {
            return false;
        }

        int insertIndex = 0;
        if (!_workspace.World.TryGetChildIndex(EntityId.Null, prefabEntity, out insertIndex))
        {
            insertIndex = _workspace.World.GetChildCount(EntityId.Null);
        }

        var record = UndoRecord.ForDeletePrefab(prefab, selectedPrefabIndex, prefabStableId, insertIndex);
        ApplyCreatePrefab(record, applyAfterValues: false);
        _undoStack.Push(record);

        int remainingPrefabCount = _workspace._prefabs.Count;
        if (remainingPrefabCount <= 0)
        {
            _workspace._selectedPrefabIndex = -1;
            _workspace._selectedPrefabEntity = EntityId.Null;
            _workspace._selectedEntities.Clear();
            return true;
        }

        int newIndex = selectedPrefabIndex;
        if (newIndex >= remainingPrefabCount)
        {
            newIndex = remainingPrefabCount - 1;
        }

        _workspace._selectedEntities.Clear();
        _workspace._selectedPrefabIndex = newIndex;

        int newPrefabId = _workspace._prefabs[newIndex].Id;
        if (UiWorkspace.TryGetEntityById(_workspace._prefabEntityById, newPrefabId, out EntityId newPrefabEntity))
        {
            _workspace._selectedPrefabEntity = newPrefabEntity;
        }
        else
        {
            _workspace._selectedPrefabEntity = EntityId.Null;
        }

        return true;
    }

    private bool TryGetPasteParentEntity(out EntityId parent)
    {
        parent = EntityId.Null;

        List<EntityId> selected = _workspace._selectedEntities;
        if (selected.Count > 0)
        {
            Span<EntityId> roots = stackalloc EntityId[32];
            int rootCount = 0;

            EntityId commonParent = EntityId.Null;
            bool hasCommonParent = true;

            for (int i = 0; i < selected.Count; i++)
            {
                EntityId entity = selected[i];
                if (entity.IsNull)
                {
                    continue;
                }

                if (HasSelectedAncestor(selected, entity))
                {
                    continue;
                }

                EntityId p = _workspace.World.GetParent(entity);
                if (p.IsNull)
                {
                    hasCommonParent = false;
                    break;
                }

                if (rootCount < roots.Length)
                {
                    roots[rootCount++] = entity;
                }

                if (commonParent.IsNull)
                {
                    commonParent = p;
                }
                else if (commonParent.Value != p.Value)
                {
                    hasCommonParent = false;
                    break;
                }
            }

            if (hasCommonParent && !commonParent.IsNull)
            {
                parent = commonParent;
                return true;
            }
        }

        if (!_workspace._selectedPrefabEntity.IsNull)
        {
            parent = _workspace._selectedPrefabEntity;
            return true;
        }

        return false;
    }

    private int CopySubtreeToClipboard(EntityId entity, int parentIndex, ClipboardDocument clipboard)
    {
        UiNodeType nodeType = _workspace.World.GetNodeType(entity);
        if (nodeType == UiNodeType.None)
        {
            return -1;
        }

        uint stableId = _workspace.World.GetStableId(entity);
        bool editorHidden = stableId != 0 && _workspace.GetLayerHidden(stableId);
        bool editorLocked = stableId != 0 && _workspace.GetLayerLocked(stableId);
        string editorName = string.Empty;
        if (stableId != 0)
        {
            _workspace.TryGetLayerName(stableId, out editorName);
        }

        UiWorkspace.GroupKind groupKind = UiWorkspace.GroupKind.Plain;
        int groupOpIndex = 0;
        if (nodeType == UiNodeType.BooleanGroup)
        {
            if (_workspace.TryGetLegacyGroupId(entity, out int groupId) && _workspace.TryGetGroupIndexById(groupId, out int groupIndex))
            {
                UiWorkspace.BooleanGroup group = _workspace._groups[groupIndex];
                groupKind = group.Kind;
                groupOpIndex = group.OpIndex;
            }
        }

        var node = new ClipboardNode
        {
            ParentIndex = parentIndex,
            NodeType = nodeType,
            EditorHidden = editorHidden,
            EditorLocked = editorLocked,
            EditorName = editorName ?? string.Empty,
            GroupKind = groupKind,
            GroupOpIndex = groupOpIndex,
            ComponentStart = clipboard.Components.Count,
            ComponentCount = 0
        };

        ulong presentMask = _workspace.World.GetComponentPresentMask(entity);
        ReadOnlySpan<AnyComponentHandle> slots = _workspace.World.GetComponentSlots(entity);

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

            int size = UiComponentClipboardPacker.GetSnapshotSize(component.Kind);
            if (size <= 0)
            {
                continue;
            }

            var data = new byte[size];
            if (!UiComponentClipboardPacker.TryPack(_workspace.PropertyWorld, component, data))
            {
                continue;
            }

            clipboard.Components.Add(new ClipboardComponent
            {
                Kind = component.Kind,
                Data = data
            });
        }

        node.ComponentCount = clipboard.Components.Count - node.ComponentStart;

        clipboard.Nodes.Add(node);
        int thisIndex = clipboard.Nodes.Count - 1;

        ReadOnlySpan<EntityId> children = _workspace.World.GetChildren(entity);
        for (int i = 0; i < children.Length; i++)
        {
            EntityId child = children[i];
            if (child.IsNull)
            {
                continue;
            }

            CopySubtreeToClipboard(child, parentIndex: thisIndex, clipboard);
        }

        return thisIndex;
    }

    private bool TryRestoreClipboard(ClipboardDocument clipboard, EntityId pasteParent, int insertIndex, bool selectPasted, bool placeAtCursor, List<EntityId>? pastedRootsOut)
    {
        if (pasteParent.IsNull)
        {
            return false;
        }

        int nodeCount = clipboard.Nodes.Count;
        if (nodeCount <= 0)
        {
            return false;
        }

        var created = new EntityId[nodeCount];
        var pastedRoots = new List<EntityId>(capacity: Math.Max(1, clipboard.RootNodeIndices.Count));

        for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
        {
            ClipboardNode node = clipboard.Nodes[nodeIndex];
            EntityId parent = node.ParentIndex < 0 ? pasteParent : created[node.ParentIndex];
            if (parent.IsNull)
            {
                return false;
            }

            EntityId newEntity = _workspace.World.CreateEntity(node.NodeType, parent);
            created[nodeIndex] = newEntity;

            uint stableId = _workspace.World.GetStableId(newEntity);

            if (node.ParentIndex < 0)
            {
                pastedRoots.Add(newEntity);
            }

            for (int i = 0; i < node.ComponentCount; i++)
            {
                ClipboardComponent src = clipboard.Components[node.ComponentStart + i];
                if (!UiComponentClipboardPacker.TryCreateComponent(_workspace.PropertyWorld, src.Kind, stableId, out AnyComponentHandle dst))
                {
                    continue;
                }

                _workspace.SetComponentWithStableId(newEntity, dst);
                UiComponentClipboardPacker.TryUnpack(_workspace.PropertyWorld, dst, src.Data);
            }

            RegisterEntityFromClipboardNode(newEntity, node);

            if (stableId != 0)
            {
                _workspace.SetLayerHidden(stableId, node.EditorHidden);
                _workspace.SetLayerLocked(stableId, node.EditorLocked);
                if (!string.IsNullOrEmpty(node.EditorName))
                {
                    _workspace.SetLayerName(stableId, node.EditorName);
                }
            }

            _workspace.EnsureDefaultLayerName(newEntity);
        }

        if (insertIndex >= 0)
        {
            for (int i = 0; i < pastedRoots.Count; i++)
            {
                EntityId root = pastedRoots[i];
                if (!_workspace.World.TryGetChildIndex(pasteParent, root, out int sourceIndex))
                {
                    continue;
                }

                int targetIndex = insertIndex + i;
                _workspace.World.MoveChild(pasteParent, sourceIndex, targetIndex);
            }
        }

        if (placeAtCursor && pastedRoots.Count > 0 && _workspace.TryGetCanvasMouseWorld(out Vector2 cursorWorld))
        {
            EntityId[] rootsArray = pastedRoots.ToArray();
            if (_workspace.TryComputeSelectionBoundsWorldEcs(pasteParent, rootsArray, out ImRect boundsWorld))
            {
                Vector2 centerWorld = new Vector2(boundsWorld.X + boundsWorld.Width * 0.5f, boundsWorld.Y + boundsWorld.Height * 0.5f);
                Vector2 deltaWorld = cursorWorld - centerWorld;
                if (deltaWorld.X != 0f || deltaWorld.Y != 0f)
                {
                    for (int i = 0; i < rootsArray.Length; i++)
                    {
                        _workspace.TranslateEntityPositionEcs(rootsArray[i], deltaWorld);
                    }
                }
            }
        }

        if (pastedRootsOut != null && pastedRoots.Count > 0)
        {
            for (int i = 0; i < pastedRoots.Count; i++)
            {
                pastedRootsOut.Add(pastedRoots[i]);
            }
        }

        if (selectPasted && pastedRoots.Count > 0)
        {
            _workspace.SelectSingleEntity(pastedRoots[0]);
            for (int i = 1; i < pastedRoots.Count; i++)
            {
                _workspace.ToggleSelectedEntity(pastedRoots[i]);
            }
        }

        return true;
    }

    private void RegisterEntityFromClipboardNode(EntityId entity, in ClipboardNode node)
    {
        if (entity.IsNull)
        {
            return;
        }

        UiNodeType nodeType = node.NodeType;

        int parentFrameId = 0;
        int parentGroupId = 0;
        int parentShapeId = 0;

        EntityId owningPrefabEntity = _workspace.FindOwningPrefabEntity(entity);
        if (!owningPrefabEntity.IsNull)
        {
            _workspace.TryGetLegacyPrefabId(owningPrefabEntity, out parentFrameId);
        }

        EntityId parentEntity = _workspace.World.GetParent(entity);
        UiNodeType parentType = parentEntity.IsNull ? UiNodeType.None : _workspace.World.GetNodeType(parentEntity);
        if (parentType == UiNodeType.BooleanGroup)
        {
            _workspace.TryGetLegacyGroupId(parentEntity, out parentGroupId);
        }
        else if (parentType == UiNodeType.Shape)
        {
            _workspace.TryGetLegacyShapeId(parentEntity, out parentShapeId);
        }

        if (nodeType == UiNodeType.Shape)
        {
            if (!_workspace.World.TryGetComponent(entity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny) ||
                !_workspace.World.TryGetComponent(entity, BlendComponent.Api.PoolIdConst, out AnyComponentHandle blendAny) ||
                !_workspace.World.TryGetComponent(entity, PaintComponent.Api.PoolIdConst, out AnyComponentHandle paintAny))
            {
                return;
            }

            ShapeKind kind = ShapeKind.Rect;
            if (_workspace.World.TryGetComponent(entity, PathComponent.Api.PoolIdConst, out _))
            {
                kind = ShapeKind.Polygon;
            }
            else if (_workspace.World.TryGetComponent(entity, CircleGeometryComponent.Api.PoolIdConst, out _))
            {
                kind = ShapeKind.Circle;
            }

            var shapeComponentView = ShapeComponent.Api.Create(_workspace.PropertyWorld, new ShapeComponent { Kind = kind });
            var shapeComponentHandle = shapeComponentView.Handle;
            _workspace.SetComponentWithStableId(entity, new AnyComponentHandle(ShapeComponent.Api.PoolIdConst, shapeComponentHandle.Index, shapeComponentHandle.Generation));

            var shape = new UiWorkspace.Shape
            {
                Id = _workspace._nextShapeId++,
                ParentFrameId = parentFrameId,
                ParentGroupId = parentGroupId,
                ParentShapeId = parentShapeId,
                Component = shapeComponentHandle,
                Transform = new TransformComponentHandle(transformAny.Index, transformAny.Generation),
                Blend = new BlendComponentHandle(blendAny.Index, blendAny.Generation),
                Paint = new PaintComponentHandle(paintAny.Index, paintAny.Generation),
                RectGeometry = default,
                CircleGeometry = default,
                Path = default,
                PointStart = -1,
                PointCount = 0
            };

            if (_workspace.World.TryGetComponent(entity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectAny))
            {
                shape.RectGeometry = new RectGeometryComponentHandle(rectAny.Index, rectAny.Generation);
            }
            if (_workspace.World.TryGetComponent(entity, CircleGeometryComponent.Api.PoolIdConst, out AnyComponentHandle circleAny))
            {
                shape.CircleGeometry = new CircleGeometryComponentHandle(circleAny.Index, circleAny.Generation);
            }
            if (_workspace.World.TryGetComponent(entity, PathComponent.Api.PoolIdConst, out AnyComponentHandle pathAny))
            {
                shape.Path = new PathComponentHandle(pathAny.Index, pathAny.Generation);
            }

            int shapeIndex = _workspace._shapes.Count;
            _workspace._shapes.Add(shape);
            UiWorkspace.SetIndexById(_workspace._shapeIndexById, shape.Id, shapeIndex);
            UiWorkspace.SetEntityById(_workspace._shapeEntityById, shape.Id, entity);
            return;
        }

        if (nodeType == UiNodeType.Text)
        {
            if (!_workspace.World.TryGetComponent(entity, TextComponent.Api.PoolIdConst, out AnyComponentHandle textAny) ||
                !_workspace.World.TryGetComponent(entity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny) ||
                !_workspace.World.TryGetComponent(entity, BlendComponent.Api.PoolIdConst, out AnyComponentHandle blendAny) ||
                !_workspace.World.TryGetComponent(entity, PaintComponent.Api.PoolIdConst, out AnyComponentHandle paintAny) ||
                !_workspace.World.TryGetComponent(entity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectAny))
            {
                return;
            }

            var text = new UiWorkspace.Text
            {
                Id = _workspace._nextTextId++,
                ParentFrameId = parentFrameId,
                ParentGroupId = parentGroupId,
                ParentShapeId = parentShapeId,
                Component = new TextComponentHandle(textAny.Index, textAny.Generation),
                Transform = new TransformComponentHandle(transformAny.Index, transformAny.Generation),
                Blend = new BlendComponentHandle(blendAny.Index, blendAny.Generation),
                Paint = new PaintComponentHandle(paintAny.Index, paintAny.Generation),
                RectGeometry = new RectGeometryComponentHandle(rectAny.Index, rectAny.Generation)
            };

            int textIndex = _workspace._texts.Count;
            _workspace._texts.Add(text);
            UiWorkspace.SetIndexById(_workspace._textIndexById, text.Id, textIndex);
            UiWorkspace.SetEntityById(_workspace._textEntityById, text.Id, entity);
            return;
        }

        if (nodeType == UiNodeType.BooleanGroup)
        {
            AnyComponentHandle booleanAny = default;
            AnyComponentHandle maskAny = default;
            bool hasBoolean = _workspace.World.TryGetComponent(entity, BooleanGroupComponent.Api.PoolIdConst, out booleanAny);
            bool hasMask = _workspace.World.TryGetComponent(entity, MaskGroupComponent.Api.PoolIdConst, out maskAny);

            if (!_workspace.World.TryGetComponent(entity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny) ||
                !_workspace.World.TryGetComponent(entity, BlendComponent.Api.PoolIdConst, out AnyComponentHandle blendAny))
            {
                return;
            }

            var group = new UiWorkspace.BooleanGroup
            {
                Id = _workspace._nextGroupId++,
                ParentFrameId = parentFrameId,
                ParentGroupId = parentGroupId,
                ParentShapeId = parentShapeId,
                Kind = node.GroupKind,
                OpIndex = node.GroupOpIndex,
                Component = hasBoolean ? new BooleanGroupComponentHandle(booleanAny.Index, booleanAny.Generation) : default,
                MaskComponent = hasMask ? new MaskGroupComponentHandle(maskAny.Index, maskAny.Generation) : default,
                Transform = new TransformComponentHandle(transformAny.Index, transformAny.Generation),
                Blend = new BlendComponentHandle(blendAny.Index, blendAny.Generation)
            };

            int groupIndex = _workspace._groups.Count;
            _workspace._groups.Add(group);
            UiWorkspace.SetIndexById(_workspace._groupIndexById, group.Id, groupIndex);
            UiWorkspace.SetEntityById(_workspace._groupEntityById, group.Id, entity);
        }
    }

    private bool HasSelectedAncestor(List<EntityId> selected, EntityId entity)
    {
        EntityId current = _workspace.World.GetParent(entity);
        int guard = 512;
        while (!current.IsNull && guard-- > 0)
        {
            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i].Value == current.Value)
                {
                    return true;
                }
            }

            current = _workspace.World.GetParent(current);
        }

        return false;
    }

    private bool DeleteSingleLayer(EntityId entity)
    {
        UiNodeType nodeType = _workspace.World.GetNodeType(entity);
        if (nodeType == UiNodeType.None || nodeType == UiNodeType.Prefab)
        {
            return false;
        }

        if (!TryBuildDeleteRecord(entity, nodeType, out UndoRecord record))
        {
            return false;
        }

        ApplyDeleteRecord(record);
        _undoStack.Push(record);
        return true;
    }

	    private void ApplyDeleteRecord(in UndoRecord record)
	    {
	        switch (record.Kind)
	        {
	            case UndoRecordKind.DeleteShape:
	                ApplyCreateShape(record, applyAfterValues: false);
	                return;

	            case UndoRecordKind.DeleteText:
	                ApplyCreateText(record, applyAfterValues: false);
	                return;

	            case UndoRecordKind.DeleteBooleanGroup:
	                ApplyCreateBooleanGroup(record, applyAfterValues: false);
	                return;

	            case UndoRecordKind.DeleteMaskGroup:
	                ApplyCreateMaskGroup(record, applyAfterValues: false);
	                return;

	            case UndoRecordKind.DeletePrefabInstance:
	                ApplyCreatePrefabInstance(record, applyAfterValues: false);
	                return;
	        }
	    }

    private bool TryBuildDeleteRecord(EntityId entity, UiNodeType nodeType, out UndoRecord record)
    {
        record = default;

        EntityId parent = _workspace.World.GetParent(entity);
        if (parent.IsNull)
        {
            return false;
        }

        if (_workspace.World.HasComponent(entity, PrefabExpandedComponent.Api.PoolIdConst))
        {
            return false;
        }

        uint entityStableId = _workspace.World.GetStableId(entity);
        uint parentStableId = _workspace.World.GetStableId(parent);
        if (entityStableId == 0 || parentStableId == 0)
        {
            return false;
        }

        int insertIndex = 0;
        if (!_workspace.World.TryGetChildIndex(parent, entity, out insertIndex))
        {
            insertIndex = _workspace.World.GetChildCount(parent);
        }

        if (nodeType == UiNodeType.Shape)
        {
            if (!_workspace.TryGetLegacyShapeId(entity, out int shapeId) || !_workspace.TryGetShapeIndexById(shapeId, out int shapeIndex))
            {
                return false;
            }

            UiWorkspace.Shape shape = _workspace._shapes[shapeIndex];
            record = UndoRecord.ForDeleteShape(shape, shapeIndex, entityStableId, parentStableId, insertIndex);
            return true;
        }

        if (nodeType == UiNodeType.Text)
        {
            if (!_workspace.TryGetLegacyTextId(entity, out int textId) || !_workspace.TryGetTextIndexById(textId, out int textIndex))
            {
                return false;
            }

            UiWorkspace.Text text = _workspace._texts[textIndex];
            record = UndoRecord.ForDeleteText(text, textIndex, entityStableId, parentStableId, insertIndex);
            return true;
        }

        if (nodeType == UiNodeType.BooleanGroup)
        {
            if (!_workspace.TryGetLegacyGroupId(entity, out int groupId) || !_workspace.TryGetGroupIndexById(groupId, out int groupIndex))
            {
                return false;
            }

            UiWorkspace.BooleanGroup group = _workspace._groups[groupIndex];
            record = group.Kind == UiWorkspace.GroupKind.Mask
                ? UndoRecord.ForDeleteMaskGroup(group, groupIndex, entityStableId, parentStableId, insertIndex)
                : UndoRecord.ForDeleteBooleanGroup(group, groupIndex, entityStableId, parentStableId, insertIndex);
            return true;
        }

        if (nodeType == UiNodeType.PrefabInstance)
        {
            record = UndoRecord.ForDeletePrefabInstance(entityStableId, parentStableId, insertIndex);
            return true;
        }

        return false;
    }

    public bool TryMoveEntityPreserveWorldTransform(EntityId entity, EntityId sourceParent, int sourceIndex, EntityId targetParent, int targetInsertIndex)
    {
        FlushPendingEdits();

        if (entity.IsNull || targetParent.IsNull)
        {
            return false;
        }

        if (entity.Value == targetParent.Value)
        {
            return false;
        }

        UiNodeType targetType = _workspace.World.GetNodeType(targetParent);
        if (targetType == UiNodeType.None)
        {
            return false;
        }

        // Prevent cycles (parenting into own subtree).
        EntityId cursor = targetParent;
        int guard = 512;
        while (!cursor.IsNull && guard-- > 0)
        {
            if (cursor.Value == entity.Value)
            {
                return false;
            }
            cursor = _workspace.World.GetParent(cursor);
        }

        EntityId actualParent = _workspace.World.GetParent(entity);
        if (actualParent.IsNull)
        {
            return false;
        }

        if (sourceParent.IsNull || sourceParent.Value != actualParent.Value)
        {
            sourceParent = actualParent;
            sourceIndex = -1;
        }

        if (sourceIndex < 0)
        {
            if (!_workspace.World.TryGetChildIndex(sourceParent, entity, out sourceIndex))
            {
                return false;
            }
        }
        else
        {
            ReadOnlySpan<EntityId> children = _workspace.World.GetChildren(sourceParent);
            if ((uint)sourceIndex >= (uint)children.Length || children[sourceIndex].Value != entity.Value)
            {
                if (!_workspace.World.TryGetChildIndex(sourceParent, entity, out sourceIndex))
                {
                    return false;
                }
            }
        }

        uint entityStableId = _workspace.World.GetStableId(entity);
        uint sourceParentStableId = _workspace.World.GetStableId(sourceParent);
        uint targetParentStableId = _workspace.World.GetStableId(targetParent);
        if (entityStableId == 0 || sourceParentStableId == 0 || targetParentStableId == 0)
        {
            return false;
        }

        bool hasTransformSnapshot = TryGetEntityTransformSnapshot(entity, out TransformComponentHandle transformHandle, out TransformComponent transformBefore);

        if (!_workspace.TryPreserveWorldTransformOnReparent(entity, sourceParent, targetParent))
        {
            return false;
        }

        _workspace.World.Reparent(entity, sourceParent, sourceIndex, targetParent, targetInsertIndex);

        int targetIndexAfter = -1;
        _workspace.World.TryGetChildIndex(targetParent, entity, out targetIndexAfter);

        TransformComponent transformAfter = default;
        if (hasTransformSnapshot)
        {
            TryGetTransformSnapshot(transformHandle, out transformAfter);
        }

        _undoStack.Push(UndoRecord.ForMoveEntity(
            entityStableId,
            sourceParentStableId,
            sourceIndex,
            targetParentStableId,
            targetInsertIndex,
            targetIndexAfter,
            transformHandle,
            transformBefore,
            transformAfter,
            hasTransformSnapshot));

        EntityId sourceOwningPrefab = _workspace.FindOwningPrefabEntity(sourceParent);
        EntityId targetOwningPrefab = _workspace.FindOwningPrefabEntity(targetParent);
        _workspace.BumpPrefabRevision(sourceOwningPrefab);
        if (targetOwningPrefab.Value != sourceOwningPrefab.Value)
        {
            _workspace.BumpPrefabRevision(targetOwningPrefab);
        }

        return true;
    }

    public int CreatePrefab(ImRect rectWorld)
    {
        FlushPendingEdits();

        int prefabListIndex = _workspace._prefabs.Count;

        var transformView = TransformComponent.Api.Create(_workspace.PropertyWorld, new TransformComponent
        {
            Position = new Vector2(rectWorld.X, rectWorld.Y),
            Scale = Vector2.One,
            Rotation = 0f,
            Anchor = new Vector2(0.5f, 0.5f),
            Depth = 0f
        });

        var prefabCanvasView = PrefabCanvasComponent.Api.Create(_workspace.PropertyWorld, new PrefabCanvasComponent
        {
            Size = new Vector2(rectWorld.Width, rectWorld.Height)
        });

        var prefab = new UiWorkspace.Prefab
        {
            Id = _workspace._nextPrefabId++,
            RectWorld = rectWorld,
            Transform = transformView.Handle,
            Animations = CreateDefaultAnimations()
        };

        _workspace._prefabs.Add(prefab);
        UiWorkspace.SetIndexById(_workspace._prefabIndexById, prefab.Id, prefabListIndex);

        int prefabInsertIndex = _workspace.World.GetChildCount(EntityId.Null);
        EntityId prefabEntity = _workspace.World.CreateEntity(UiNodeType.Prefab, EntityId.Null);
        EnsureClipRectArchetypeComponent(prefabEntity);
        EnsureDraggableArchetypeComponent(prefabEntity);
        _workspace.SetComponentWithStableId(prefabEntity, TransformComponentProperties.ToAnyHandle(prefab.Transform));
        _workspace.SetComponentWithStableId(prefabEntity, PrefabCanvasComponentProperties.ToAnyHandle(prefabCanvasView.Handle));
        var revisionView = PrefabRevisionComponent.Api.Create(_workspace.PropertyWorld, new PrefabRevisionComponent { Revision = 1 });
        _workspace.SetComponentWithStableId(prefabEntity, new AnyComponentHandle(PrefabRevisionComponent.Api.PoolIdConst, revisionView.Handle.Index, revisionView.Handle.Generation));
        var variablesView = PrefabVariablesComponent.Api.Create(_workspace.PropertyWorld, new PrefabVariablesComponent { NextVariableId = 1, VariableCount = 0 });
        _workspace.SetComponentWithStableId(prefabEntity, new AnyComponentHandle(PrefabVariablesComponent.Api.PoolIdConst, variablesView.Handle.Index, variablesView.Handle.Generation));
        var bindingsView = PrefabBindingsComponent.Api.Create(_workspace.PropertyWorld, new PrefabBindingsComponent { Revision = 1, BindingCount = 0 });
        _workspace.SetComponentWithStableId(prefabEntity, new AnyComponentHandle(PrefabBindingsComponent.Api.PoolIdConst, bindingsView.Handle.Index, bindingsView.Handle.Generation));

        uint prefabStableIdForStateMachine = _workspace.World.GetStableId(prefabEntity);
        var stateMachineInit = StateMachineDefinitionComponent.CreateDefault();
        var stateMachineView = StateMachineDefinitionComponent.Api.CreateWithId(
            _workspace.PropertyWorld,
            new StateMachineDefinitionComponentId((ulong)prefabStableIdForStateMachine),
            stateMachineInit);
        _workspace.SetComponentWithStableId(prefabEntity, new AnyComponentHandle(StateMachineDefinitionComponent.Api.PoolIdConst, stateMachineView.Handle.Index, stateMachineView.Handle.Generation));

        uint prefabStableIdForAnimations = _workspace.World.GetStableId(prefabEntity);
        var animationsInit = AnimationLibraryComponent.CreateDefault();
        var animationsView = AnimationLibraryComponent.Api.CreateWithId(
            _workspace.PropertyWorld,
            new AnimationLibraryComponentId((ulong)prefabStableIdForAnimations),
            animationsInit);
        _workspace.SetComponentWithStableId(prefabEntity, new AnyComponentHandle(AnimationLibraryComponent.Api.PoolIdConst, animationsView.Handle.Index, animationsView.Handle.Generation));
        AnimationLibraryComponent.ViewProxy lib = animationsView;
        prefab.AnimationsCacheRevision = AnimationLibraryOps.ExportFromDocument(prefab.Animations, lib);
        UiWorkspace.SetEntityById(_workspace._prefabEntityById, prefab.Id, prefabEntity);
        _workspace.EnsureDefaultLayerName(prefabEntity);

        uint prefabStableId = _workspace.World.GetStableId(prefabEntity);
        _undoStack.Push(UndoRecord.ForCreatePrefab(prefab, prefabListIndex, prefabStableId, prefabInsertIndex));
        return prefab.Id;
    }

    public bool TryEnsurePrefabVariablesComponent(EntityId prefabEntity, out AnyComponentHandle component)
    {
        component = default;

        if (prefabEntity.IsNull || _workspace.World.GetNodeType(prefabEntity) != UiNodeType.Prefab)
        {
            return false;
        }

        if (_workspace.World.TryGetComponent(prefabEntity, PrefabVariablesComponent.Api.PoolIdConst, out component) && component.IsValid)
        {
            return true;
        }

        var created = PrefabVariablesComponent.Api.Create(_workspace.PropertyWorld, new PrefabVariablesComponent { NextVariableId = 1, VariableCount = 0 });
        component = new AnyComponentHandle(PrefabVariablesComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation);
        _workspace.SetComponentWithStableId(prefabEntity, component);
        return true;
    }

    public bool TryEnsureAnimationLibraryComponent(EntityId prefabEntity, out AnyComponentHandle component)
    {
        component = default;

        if (prefabEntity.IsNull || _workspace.World.GetNodeType(prefabEntity) != UiNodeType.Prefab)
        {
            return false;
        }

        if (_workspace.World.TryGetComponent(prefabEntity, AnimationLibraryComponent.Api.PoolIdConst, out component) && component.IsValid)
        {
            return true;
        }

        uint prefabStableId = _workspace.World.GetStableId(prefabEntity);
        if (prefabStableId == 0)
        {
            return false;
        }

        var created = AnimationLibraryComponent.Api.CreateWithId(
            _workspace.PropertyWorld,
            new AnimationLibraryComponentId((ulong)prefabStableId),
            AnimationLibraryComponent.CreateDefault());

        component = new AnyComponentHandle(AnimationLibraryComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation);
        _workspace.SetComponentWithStableId(prefabEntity, component);

        if (_workspace._selectedPrefabIndex >= 0 && _workspace._selectedPrefabIndex < _workspace._prefabs.Count)
        {
            int prefabIndex = _workspace._selectedPrefabIndex;
            UiWorkspace.Prefab prefab = _workspace._prefabs[prefabIndex];
            if (prefab.Animations == null)
            {
                prefab.Animations = new AnimationDocument();
            }

            AnimationLibraryComponent.ViewProxy lib = created;
            prefab.AnimationsCacheRevision = AnimationLibraryOps.ExportFromDocument(prefab.Animations, lib);
            _workspace._prefabs[prefabIndex] = prefab;
        }

        return true;
    }

    public bool TryEnsurePrefabBindingsComponent(EntityId prefabEntity, out AnyComponentHandle component)
    {
        component = default;

        if (prefabEntity.IsNull || _workspace.World.GetNodeType(prefabEntity) != UiNodeType.Prefab)
        {
            return false;
        }

        if (_workspace.World.TryGetComponent(prefabEntity, PrefabBindingsComponent.Api.PoolIdConst, out component) && component.IsValid)
        {
            return true;
        }

        var created = PrefabBindingsComponent.Api.Create(_workspace.PropertyWorld, new PrefabBindingsComponent { Revision = 1, BindingCount = 0 });
        component = new AnyComponentHandle(PrefabBindingsComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation);
        _workspace.SetComponentWithStableId(prefabEntity, component);
        return true;
    }

    public bool TryEnsurePrefabListDataComponent(EntityId entity, out AnyComponentHandle component)
    {
        component = default;

        if (entity.IsNull)
        {
            return false;
        }

        UiNodeType nodeType = _workspace.World.GetNodeType(entity);
        if (nodeType != UiNodeType.Prefab && nodeType != UiNodeType.PrefabInstance)
        {
            return false;
        }

        if (_workspace.World.TryGetComponent(entity, PrefabListDataComponent.Api.PoolIdConst, out component) && component.IsValid)
        {
            return true;
        }

        var created = PrefabListDataComponent.Api.Create(_workspace.PropertyWorld, new PrefabListDataComponent { EntryCount = 0 });
        component = new AnyComponentHandle(PrefabListDataComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation);
        _workspace.SetComponentWithStableId(entity, component);
        return true;
    }

    public bool TryEnsureEventListenerComponent(EntityId entity, out AnyComponentHandle component)
    {
        component = default;

        if (entity.IsNull || _workspace.World.GetNodeType(entity) == UiNodeType.None)
        {
            return false;
        }

        if (_workspace.World.TryGetComponent(entity, EventListenerComponent.Api.PoolIdConst, out component) && component.IsValid)
        {
            return true;
        }

        var created = EventListenerComponent.Api.Create(_workspace.PropertyWorld, default(EventListenerComponent));
        component = new AnyComponentHandle(EventListenerComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation);
        _workspace.SetComponentWithStableId(entity, component);
        _workspace.BumpPrefabRevision(_workspace.FindOwningPrefabEntity(entity));
        return true;
    }

    public bool TryEnsurePrefabInstancePropertyOverridesComponent(EntityId entity, out AnyComponentHandle component)
    {
        component = default;

        if (entity.IsNull || _workspace.World.GetNodeType(entity) != UiNodeType.PrefabInstance)
        {
            return false;
        }

        if (_workspace.World.TryGetComponent(entity, PrefabInstancePropertyOverridesComponent.Api.PoolIdConst, out component) && component.IsValid)
        {
            return true;
        }

        var created = PrefabInstancePropertyOverridesComponent.Api.Create(_workspace.PropertyWorld, new PrefabInstancePropertyOverridesComponent
        {
            Count = 0,
            RootOverrideCount = 0
        });

        component = new AnyComponentHandle(PrefabInstancePropertyOverridesComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation);
        _workspace.SetComponentWithStableId(entity, component);
        return true;
    }

    private void EnsureClipRectArchetypeComponent(EntityId entity)
    {
        if (entity.IsNull)
        {
            return;
        }

        UiNodeType type = _workspace.World.GetNodeType(entity);
        if (type != UiNodeType.Shape && type != UiNodeType.BooleanGroup && type != UiNodeType.Prefab)
        {
            return;
        }

        if (_workspace.World.TryGetComponent(entity, ClipRectComponent.Api.PoolIdConst, out AnyComponentHandle existing) && existing.IsValid)
        {
            return;
        }

        bool enabled = type == UiNodeType.Prefab;
        var created = ClipRectComponent.Api.Create(_workspace.PropertyWorld, new ClipRectComponent
        {
            Enabled = enabled
        });
        var handle = new AnyComponentHandle(ClipRectComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation);
        _workspace.SetComponentWithStableId(entity, handle);
    }

    private void EnsureDraggableArchetypeComponent(EntityId entity)
    {
        if (entity.IsNull)
        {
            return;
        }

        UiNodeType type = _workspace.World.GetNodeType(entity);
        if (type != UiNodeType.Shape && type != UiNodeType.BooleanGroup && type != UiNodeType.Prefab)
        {
            return;
        }

        if (_workspace.World.TryGetComponent(entity, DraggableComponent.Api.PoolIdConst, out AnyComponentHandle existing) && existing.IsValid)
        {
            return;
        }

        var created = DraggableComponent.Api.Create(_workspace.PropertyWorld, new DraggableComponent
        {
            Enabled = false
        });
        var handle = new AnyComponentHandle(DraggableComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation);
        _workspace.SetComponentWithStableId(entity, handle);
    }

    private void EnsureConstraintListArchetypeComponent(EntityId entity)
    {
        if (entity.IsNull)
        {
            return;
        }

        UiNodeType type = _workspace.World.GetNodeType(entity);
        if (type != UiNodeType.Shape && type != UiNodeType.BooleanGroup && type != UiNodeType.Text && type != UiNodeType.PrefabInstance)
        {
            return;
        }

        if (_workspace.World.TryGetComponent(entity, ConstraintListComponent.Api.PoolIdConst, out AnyComponentHandle existing) && existing.IsValid)
        {
            return;
        }

        var created = ConstraintListComponent.Api.Create(_workspace.PropertyWorld, new ConstraintListComponent { Count = 0 });
        var handle = new AnyComponentHandle(ConstraintListComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation);
        _workspace.SetComponentWithStableId(entity, handle);
    }


    public bool AddPrefabVariable(EntityId prefabEntity)
    {
        return AddPrefabVariable(prefabEntity, PropertyKind.Float);
    }

    public bool AddPrefabVariable(EntityId prefabEntity, PropertyKind kind)
    {
        FlushPendingEdits();

        if (!TryEnsurePrefabVariablesComponent(prefabEntity, out AnyComponentHandle varsAny))
        {
            return false;
        }

        uint stableId = _workspace.World.GetStableId(prefabEntity);
        if (stableId == 0)
        {
            return false;
        }

        int size = UiComponentClipboardPacker.GetSnapshotSize(PrefabVariablesComponent.Api.PoolIdConst);
        if (size <= 0)
        {
            return false;
        }

        var beforeBytes = new byte[size];
        UiComponentClipboardPacker.TryPack(_workspace.PropertyWorld, varsAny, beforeBytes);

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        var vars = PrefabVariablesComponent.Api.FromHandle(_workspace.PropertyWorld, varsHandle);
        if (!vars.IsAlive)
        {
            return false;
        }

        ushort count = vars.VariableCount;
        if (count >= PrefabVariablesComponent.MaxVariables)
        {
            _workspace.ShowToast("Max variables reached");
            return false;
        }

        Span<ushort> ids = vars.VariableIdSpan();
        Span<StringHandle> names = vars.NameSpan();
        Span<int> kinds = vars.KindSpan();
        Span<PropertyValue> defaults = vars.DefaultValueSpan();

        ushort nextId = vars.NextVariableId;
        if (nextId == 0)
        {
            nextId = 1;
        }

        ushort assignedId = nextId;
        for (int attempts = 0; attempts < PrefabVariablesComponent.MaxVariables + 1; attempts++)
        {
            bool used = false;
            for (int i = 0; i < count; i++)
            {
                if (ids[i] == assignedId)
                {
                    used = true;
                    break;
                }
            }
            if (!used)
            {
                break;
            }
            assignedId++;
            if (assignedId == 0)
            {
                assignedId = 1;
            }
        }

        ids[count] = assignedId;
        names[count] = $"Var {assignedId}";
        kinds[count] = (int)kind;
        defaults[count] = GetDefaultPrefabVariableValue(kind);
        vars.VariableCount = (ushort)(count + 1);
        vars.NextVariableId = (ushort)(assignedId + 1);

        var afterBytes = new byte[size];
        UiComponentClipboardPacker.TryPack(_workspace.PropertyWorld, varsAny, afterBytes);

        int payloadIndex = _undoComponentSnapshotPayload.Count;
        _undoComponentSnapshotPayload.Add(new ComponentSnapshotUndoPayload
        {
            EntityStableId = stableId,
            ComponentKind = PrefabVariablesComponent.Api.PoolIdConst,
            Before = beforeBytes,
            After = afterBytes
        });
        _undoStack.Push(UndoRecord.ForComponentSnapshot(payloadIndex));
        _workspace.BumpPrefabRevision(prefabEntity);
        return true;
    }

    private static PropertyValue GetDefaultPrefabVariableValue(PropertyKind kind)
    {
        return kind switch
        {
            PropertyKind.Float => PropertyValue.FromFloat(0f),
            PropertyKind.Int => PropertyValue.FromInt(0),
            PropertyKind.Bool => PropertyValue.FromBool(false),
            PropertyKind.Trigger => PropertyValue.FromBool(false),
            PropertyKind.Vec2 => PropertyValue.FromVec2(default),
            PropertyKind.Vec3 => PropertyValue.FromVec3(default),
            PropertyKind.Vec4 => PropertyValue.FromVec4(default),
            PropertyKind.Color32 => PropertyValue.FromColor32(new Color32(255, 255, 255, 255)),
            PropertyKind.StringHandle => PropertyValue.FromStringHandle(default),
            PropertyKind.Fixed64 => PropertyValue.FromFixed64(default),
            PropertyKind.Fixed64Vec2 => PropertyValue.FromFixed64Vec2(default),
            PropertyKind.Fixed64Vec3 => PropertyValue.FromFixed64Vec3(default),
            PropertyKind.PrefabRef => PropertyValue.FromUInt(0),
            PropertyKind.ShapeRef => PropertyValue.FromUInt(0),
            PropertyKind.List => PropertyValue.FromUInt(0),
            _ => default
        };
    }

    public bool RemovePrefabVariable(EntityId prefabEntity, ushort variableId)
    {
        FlushPendingEdits();

        if (variableId == 0)
        {
            return false;
        }

        if (!TryEnsurePrefabVariablesComponent(prefabEntity, out AnyComponentHandle varsAny))
        {
            return false;
        }

        uint stableId = _workspace.World.GetStableId(prefabEntity);
        if (stableId == 0)
        {
            return false;
        }

        int varsSize = UiComponentClipboardPacker.GetSnapshotSize(PrefabVariablesComponent.Api.PoolIdConst);
        if (varsSize <= 0)
        {
            return false;
        }

        var varsBeforeBytes = new byte[varsSize];
        UiComponentClipboardPacker.TryPack(_workspace.PropertyWorld, varsAny, varsBeforeBytes);

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        var vars = PrefabVariablesComponent.Api.FromHandle(_workspace.PropertyWorld, varsHandle);
        if (!vars.IsAlive)
        {
            return false;
        }

        ushort count = vars.VariableCount;
        if (count == 0)
        {
            return false;
        }

        Span<ushort> ids = vars.VariableIdSpan();
        Span<StringHandle> names = vars.NameSpan();
        Span<int> kinds = vars.KindSpan();
        Span<PropertyValue> defaults = vars.DefaultValueSpan();

        int removeIndex = -1;
        int n = Math.Min(count, (ushort)PrefabVariablesComponent.MaxVariables);
        for (int i = 0; i < n; i++)
        {
            if (ids[i] == variableId)
            {
                removeIndex = i;
                break;
            }
        }

        if (removeIndex < 0)
        {
            return false;
        }

        PropertyKind removedKind = (PropertyKind)kinds[removeIndex];

        for (int i = removeIndex; i < n - 1; i++)
        {
            ids[i] = ids[i + 1];
            names[i] = names[i + 1];
            kinds[i] = kinds[i + 1];
            defaults[i] = defaults[i + 1];
        }

        int last = n - 1;
        ids[last] = 0;
        names[last] = default;
        kinds[last] = 0;
        defaults[last] = default;
        vars.VariableCount = (ushort)(n - 1);

        if (removedKind == PropertyKind.List &&
            _workspace.World.TryGetComponent(prefabEntity, PrefabListDataComponent.Api.PoolIdConst, out AnyComponentHandle listAny) &&
            listAny.IsValid)
        {
            int listSize = UiComponentClipboardPacker.GetSnapshotSize(PrefabListDataComponent.Api.PoolIdConst);
            if (listSize <= 0)
            {
                return false;
            }

            var listBeforeBytes = new byte[listSize];
            UiComponentClipboardPacker.TryPack(_workspace.PropertyWorld, listAny, listBeforeBytes);

            var listHandle = new PrefabListDataComponentHandle(listAny.Index, listAny.Generation);
            var list = PrefabListDataComponent.Api.FromHandle(_workspace.PropertyWorld, listHandle);
            if (list.IsAlive)
            {
                RemoveListEntryIfExists(list, variableId);
            }

            var varsAfterBytes = new byte[varsSize];
            UiComponentClipboardPacker.TryPack(_workspace.PropertyWorld, varsAny, varsAfterBytes);
            var listAfterBytes = new byte[listSize];
            UiComponentClipboardPacker.TryPack(_workspace.PropertyWorld, listAny, listAfterBytes);

            int payloadStart = _undoComponentSnapshotPayload.Count;
            _undoComponentSnapshotPayload.Add(new ComponentSnapshotUndoPayload
            {
                EntityStableId = stableId,
                ComponentKind = PrefabVariablesComponent.Api.PoolIdConst,
                Before = varsBeforeBytes,
                After = varsAfterBytes
            });
            _undoComponentSnapshotPayload.Add(new ComponentSnapshotUndoPayload
            {
                EntityStableId = stableId,
                ComponentKind = PrefabListDataComponent.Api.PoolIdConst,
                Before = listBeforeBytes,
                After = listAfterBytes
            });
            _undoStack.Push(UndoRecord.ForComponentSnapshotMany(payloadStart, payloadCount: 2));
        }
        else
        {
            var varsAfterBytes = new byte[varsSize];
            UiComponentClipboardPacker.TryPack(_workspace.PropertyWorld, varsAny, varsAfterBytes);

            int payloadIndex = _undoComponentSnapshotPayload.Count;
            _undoComponentSnapshotPayload.Add(new ComponentSnapshotUndoPayload
            {
                EntityStableId = stableId,
                ComponentKind = PrefabVariablesComponent.Api.PoolIdConst,
                Before = varsBeforeBytes,
                After = varsAfterBytes
            });
            _undoStack.Push(UndoRecord.ForComponentSnapshot(payloadIndex));
        }

        _workspace.BumpPrefabRevision(prefabEntity);
        return true;
    }

    public void SetPrefabVariableName(int widgetId, bool isEditing, EntityId prefabEntity, ushort variableId, StringHandle name)
    {
        if (variableId == 0 || prefabEntity.IsNull)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        if (!TryEnsurePrefabVariablesComponent(prefabEntity, out AnyComponentHandle varsAny))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        uint stableId = _workspace.World.GetStableId(prefabEntity);
        if (stableId == 0)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        var vars = PrefabVariablesComponent.Api.FromHandle(_workspace.PropertyWorld, varsHandle);
        if (!vars.IsAlive)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        ushort count = vars.VariableCount;
        int n = Math.Min(count, (ushort)PrefabVariablesComponent.MaxVariables);
        Span<ushort> ids = vars.VariableIdSpan();
        Span<StringHandle> names = vars.NameSpan();

        int index = -1;
        for (int i = 0; i < n; i++)
        {
            if (ids[i] == variableId)
            {
                index = i;
                break;
            }
        }

        if (index < 0 || names[index] == name)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        int payloadIndex = BeginOrUpdateComponentSnapshotEdit(widgetId, stableId, varsAny);
        names[index] = name;
        UpdateComponentSnapshotAfter(payloadIndex, varsAny);

        if (!isEditing)
        {
            CommitPendingEdit();
            return;
        }

        NotifyPropertyWidgetState(widgetId, isEditing);
    }

    public void SetPrefabVariableKind(int widgetId, bool isEditing, EntityId prefabEntity, ushort variableId, PropertyKind kind)
    {
        if (variableId == 0 || prefabEntity.IsNull)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        if (!TryEnsurePrefabVariablesComponent(prefabEntity, out AnyComponentHandle varsAny))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        uint stableId = _workspace.World.GetStableId(prefabEntity);
        if (stableId == 0)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        var vars = PrefabVariablesComponent.Api.FromHandle(_workspace.PropertyWorld, varsHandle);
        if (!vars.IsAlive)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        ushort count = vars.VariableCount;
        int n = Math.Min(count, (ushort)PrefabVariablesComponent.MaxVariables);
        Span<ushort> ids = vars.VariableIdSpan();
        Span<int> kinds = vars.KindSpan();
        Span<PropertyValue> defaults = vars.DefaultValueSpan();

        int index = -1;
        for (int i = 0; i < n; i++)
        {
            if (ids[i] == variableId)
            {
                index = i;
                break;
            }
        }

        if (index < 0 || kinds[index] == (int)kind)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        int payloadIndex = BeginOrUpdateComponentSnapshotEdit(widgetId, stableId, varsAny);
        kinds[index] = (int)kind;
        defaults[index] = default;
        UpdateComponentSnapshotAfter(payloadIndex, varsAny);

        if (!isEditing)
        {
            CommitPendingEdit();
            return;
        }

        NotifyPropertyWidgetState(widgetId, isEditing);
    }

    public void SetPrefabVariableDefault(int widgetId, bool isEditing, EntityId prefabEntity, ushort variableId, PropertyKind kind, PropertyValue value)
    {
        if (variableId == 0 || prefabEntity.IsNull)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        if (!TryEnsurePrefabVariablesComponent(prefabEntity, out AnyComponentHandle varsAny))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        uint stableId = _workspace.World.GetStableId(prefabEntity);
        if (stableId == 0)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        var vars = PrefabVariablesComponent.Api.FromHandle(_workspace.PropertyWorld, varsHandle);
        if (!vars.IsAlive)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        ushort count = vars.VariableCount;
        int n = Math.Min(count, (ushort)PrefabVariablesComponent.MaxVariables);
        Span<ushort> ids = vars.VariableIdSpan();
        Span<PropertyValue> defaults = vars.DefaultValueSpan();

        int index = -1;
        for (int i = 0; i < n; i++)
        {
            if (ids[i] == variableId)
            {
                index = i;
                break;
            }
        }

        if (index < 0 || defaults[index].Equals(kind, value))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        int payloadIndex = BeginOrUpdateComponentSnapshotEdit(widgetId, stableId, varsAny);
        defaults[index] = value;
        UpdateComponentSnapshotAfter(payloadIndex, varsAny);

        if (!isEditing)
        {
            CommitPendingEdit();
            return;
        }

        NotifyPropertyWidgetState(widgetId, isEditing);
    }

    public void SetPrefabInstanceVariableValue(int widgetId, bool isEditing, EntityId instanceEntity, ushort variableId, PropertyKind kind, PropertyValue value)
    {
        if (variableId == 0 || instanceEntity.IsNull)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        if (!_workspace.World.TryGetComponent(instanceEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) ||
            !instanceAny.IsValid)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        uint stableId = _workspace.World.GetStableId(instanceEntity);
        if (stableId == 0)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
        var instance = PrefabInstanceComponent.Api.FromHandle(_workspace.PropertyWorld, instanceHandle);
        if (!instance.IsAlive)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
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

        if (index >= 0 && values[index].Equals(kind, value) && (overrideMask & (1UL << index)) != 0)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        int payloadIndex = BeginOrUpdateComponentSnapshotEdit(widgetId, stableId, instanceAny);
        if (index < 0)
        {
            if (n >= PrefabInstanceComponent.MaxVariables)
            {
                _workspace.ShowToast("Max variable overrides reached");
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            index = n;
            ids[index] = variableId;
            instance.ValueCount = (ushort)(n + 1);
        }

        values[index] = value;
        instance.OverrideMask = overrideMask | (1UL << index);
        UpdateComponentSnapshotAfter(payloadIndex, instanceAny);
        AutoKeyPrefabInstanceVariableEdited(instanceEntity, variableId, kind, value);

        if (!isEditing)
        {
            CommitPendingEdit();
            return;
        }

        NotifyPropertyWidgetState(widgetId, isEditing);
    }

    public void SetPrefabListType(int widgetId, bool isEditing, EntityId entity, ushort variableId, uint typePrefabStableId)
    {
        if (variableId == 0 || entity.IsNull)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        UiNodeType nodeType = _workspace.World.GetNodeType(entity);
        if (nodeType == UiNodeType.Prefab)
        {
            if (!TryEnsurePrefabVariablesComponent(entity, out AnyComponentHandle varsAny) || !varsAny.IsValid)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            if (!TryEnsurePrefabListDataComponent(entity, out AnyComponentHandle listAny) || !listAny.IsValid)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            uint stableId = _workspace.World.GetStableId(entity);
            if (stableId == 0)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            if (typePrefabStableId != 0 && typePrefabStableId == stableId)
            {
                _workspace.ShowToast("List type cannot be self");
                typePrefabStableId = 0;
            }

            var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
            var vars = PrefabVariablesComponent.Api.FromHandle(_workspace.PropertyWorld, varsHandle);
            if (!vars.IsAlive)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            ushort count = vars.VariableCount;
            int n = Math.Min(count, (ushort)PrefabVariablesComponent.MaxVariables);
            ReadOnlySpan<ushort> ids = vars.VariableIdReadOnlySpan();
            Span<int> kinds = vars.KindSpan();
            Span<PropertyValue> defaults = vars.DefaultValueSpan();

            int varIndex = FindIndex(ids, n, variableId);
            if (varIndex < 0 || (PropertyKind)kinds[varIndex] != PropertyKind.List)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            ushort fieldCount = 0;
            if (typePrefabStableId != 0)
            {
                Span<PropertyValue> fieldDefaults = stackalloc PropertyValue[PrefabListDataComponent.MaxFieldsPerItem];
                if (!TryGetListTypeFieldDefaults(typePrefabStableId, fieldDefaults, out fieldCount))
                {
                    NotifyPropertyWidgetState(widgetId, isEditing);
                    return;
                }
            }

            Span<AnyComponentHandle> components = stackalloc AnyComponentHandle[2] { varsAny, listAny };
            int payloadStart = BeginOrUpdateComponentSnapshotManyEdit(widgetId, stableId, components);
            if (payloadStart < 0)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            defaults[varIndex] = PropertyValue.FromUInt(typePrefabStableId);

            var listHandle = new PrefabListDataComponentHandle(listAny.Index, listAny.Generation);
            var list = PrefabListDataComponent.Api.FromHandle(_workspace.PropertyWorld, listHandle);
            if (list.IsAlive)
            {
                if (typePrefabStableId == 0)
                {
                    RemoveListEntryIfExists(list, variableId);
                }
                else
                {
                    ResetListEntry(list, variableId, fieldCount);
                }
            }

            UpdateComponentSnapshotManyAfter(payloadStart, components);

            if (!isEditing)
            {
                CommitPendingEdit();
                return;
            }

            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        if (nodeType == UiNodeType.PrefabInstance)
        {
            if (!_workspace.World.TryGetComponent(entity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) ||
                !instanceAny.IsValid)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            if (!TryEnsurePrefabListDataComponent(entity, out AnyComponentHandle listAny) || !listAny.IsValid)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            uint stableId = _workspace.World.GetStableId(entity);
            if (stableId == 0)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var instance = PrefabInstanceComponent.Api.FromHandle(_workspace.PropertyWorld, instanceHandle);
            if (!instance.IsAlive)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            if (typePrefabStableId != 0 && instance.SourcePrefabStableId != 0 && typePrefabStableId == instance.SourcePrefabStableId)
            {
                _workspace.ShowToast("List type cannot be self");
                typePrefabStableId = 0;
            }

            ushort count = instance.ValueCount;
            int n = Math.Min(count, (ushort)PrefabInstanceComponent.MaxVariables);
            Span<ushort> ids = instance.VariableIdSpan();
            Span<PropertyValue> values = instance.ValueSpan();

            int varIndex = FindIndex(ids, n, variableId);
            if (varIndex < 0)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            ushort fieldCount = 0;
            if (typePrefabStableId != 0)
            {
                Span<PropertyValue> fieldDefaults = stackalloc PropertyValue[PrefabListDataComponent.MaxFieldsPerItem];
                if (!TryGetListTypeFieldDefaults(typePrefabStableId, fieldDefaults, out fieldCount))
                {
                    NotifyPropertyWidgetState(widgetId, isEditing);
                    return;
                }
            }

            Span<AnyComponentHandle> components = stackalloc AnyComponentHandle[2] { instanceAny, listAny };
            int payloadStart = BeginOrUpdateComponentSnapshotManyEdit(widgetId, stableId, components);
            if (payloadStart < 0)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            values[varIndex] = PropertyValue.FromUInt(typePrefabStableId);
            instance.OverrideMask = instance.OverrideMask | (1UL << varIndex);

            var listHandle = new PrefabListDataComponentHandle(listAny.Index, listAny.Generation);
            var list = PrefabListDataComponent.Api.FromHandle(_workspace.PropertyWorld, listHandle);
            if (list.IsAlive)
            {
                if (typePrefabStableId == 0)
                {
                    RemoveListEntryIfExists(list, variableId);
                }
                else
                {
                    ResetListEntry(list, variableId, fieldCount);
                }
            }

            UpdateComponentSnapshotManyAfter(payloadStart, components);

            if (!isEditing)
            {
                CommitPendingEdit();
                return;
            }

            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        NotifyPropertyWidgetState(widgetId, isEditing);
    }

    public void AddPrefabListItem(int widgetId, EntityId entity, ushort variableId)
    {
        FlushPendingEdits();

        if (variableId == 0 || entity.IsNull)
        {
            return;
        }

        UiNodeType nodeType = _workspace.World.GetNodeType(entity);
        if (nodeType == UiNodeType.Prefab)
        {
            if (!TryEnsurePrefabVariablesComponent(entity, out AnyComponentHandle varsAny) || !varsAny.IsValid)
            {
                return;
            }

            if (!TryEnsurePrefabListDataComponent(entity, out AnyComponentHandle listAny) || !listAny.IsValid)
            {
                return;
            }

            uint stableId = _workspace.World.GetStableId(entity);
            if (stableId == 0)
            {
                return;
            }

            var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
            var vars = PrefabVariablesComponent.Api.FromHandle(_workspace.PropertyWorld, varsHandle);
            if (!vars.IsAlive)
            {
                return;
            }

            ushort varCount = vars.VariableCount;
            int vn = Math.Min(varCount, (ushort)PrefabVariablesComponent.MaxVariables);
            ReadOnlySpan<ushort> varIds = vars.VariableIdReadOnlySpan();
            ReadOnlySpan<int> kinds = vars.KindReadOnlySpan();
            ReadOnlySpan<PropertyValue> defaults = vars.DefaultValueReadOnlySpan();

            int varIndex = FindIndex(varIds, vn, variableId);
            if (varIndex < 0 || (PropertyKind)kinds[varIndex] != PropertyKind.List)
            {
                return;
            }

            uint typeStableId = defaults[varIndex].UInt;
            if (typeStableId == 0)
            {
                _workspace.ShowToast("List type is missing");
                return;
            }

            Span<PropertyValue> fieldDefaults = stackalloc PropertyValue[PrefabListDataComponent.MaxFieldsPerItem];
            if (!TryGetListTypeFieldDefaults(typeStableId, fieldDefaults, out ushort fieldCount))
            {
                return;
            }

            var listHandle = new PrefabListDataComponentHandle(listAny.Index, listAny.Generation);
            var list = PrefabListDataComponent.Api.FromHandle(_workspace.PropertyWorld, listHandle);
            if (!list.IsAlive)
            {
                return;
            }

            int payloadIndex = BeginOrUpdateComponentSnapshotEdit(widgetId, stableId, listAny);
            if (payloadIndex < 0)
            {
                return;
            }

            if (!TryEnsureListEntrySchema(list, variableId, fieldDefaults, fieldCount))
            {
                _workspace.ShowToast("List schema is invalid");
                UpdateComponentSnapshotAfter(payloadIndex, listAny);
                CommitPendingEdit();
                return;
            }

            if (!TryAppendListItem(list, variableId, fieldDefaults, fieldCount))
            {
                _workspace.ShowToast("List is full");
                UpdateComponentSnapshotAfter(payloadIndex, listAny);
                CommitPendingEdit();
                return;
            }

            UpdateComponentSnapshotAfter(payloadIndex, listAny);
            CommitPendingEdit();
            return;
        }

        if (nodeType == UiNodeType.PrefabInstance)
        {
            if (!_workspace.World.TryGetComponent(entity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) ||
                !instanceAny.IsValid)
            {
                return;
            }

            if (!TryEnsurePrefabListDataComponent(entity, out AnyComponentHandle listAny) || !listAny.IsValid)
            {
                return;
            }

            uint stableId = _workspace.World.GetStableId(entity);
            if (stableId == 0)
            {
                return;
            }

            var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var instance = PrefabInstanceComponent.Api.FromHandle(_workspace.PropertyWorld, instanceHandle);
            if (!instance.IsAlive)
            {
                return;
            }

            ushort count = instance.ValueCount;
            int n = Math.Min(count, (ushort)PrefabInstanceComponent.MaxVariables);
            ReadOnlySpan<ushort> ids = instance.VariableIdReadOnlySpan();
            ReadOnlySpan<PropertyValue> values = instance.ValueReadOnlySpan();

            int varIndex = FindIndex(ids, n, variableId);
            if (varIndex < 0)
            {
                return;
            }

            uint typeStableId = values[varIndex].UInt;
            if (typeStableId == 0)
            {
                _workspace.ShowToast("List type is missing");
                return;
            }

            Span<PropertyValue> fieldDefaults = stackalloc PropertyValue[PrefabListDataComponent.MaxFieldsPerItem];
            if (!TryGetListTypeFieldDefaults(typeStableId, fieldDefaults, out ushort fieldCount))
            {
                return;
            }

            Span<AnyComponentHandle> components = stackalloc AnyComponentHandle[2] { instanceAny, listAny };
            int payloadStart = BeginOrUpdateComponentSnapshotManyEdit(widgetId, stableId, components);
            if (payloadStart < 0)
            {
                return;
            }

            var listHandle = new PrefabListDataComponentHandle(listAny.Index, listAny.Generation);
            var list = PrefabListDataComponent.Api.FromHandle(_workspace.PropertyWorld, listHandle);
            if (!list.IsAlive)
            {
                UpdateComponentSnapshotManyAfter(payloadStart, components);
                CommitPendingEdit();
                return;
            }

            EnsureInstanceListOverride(entity, instance, variableId, varIndex, ref list);
            instance.OverrideMask = instance.OverrideMask | (1UL << varIndex);

            if (!TryEnsureListEntrySchema(list, variableId, fieldDefaults, fieldCount))
            {
                _workspace.ShowToast("List schema is invalid");
                UpdateComponentSnapshotManyAfter(payloadStart, components);
                CommitPendingEdit();
                return;
            }

            if (!TryAppendListItem(list, variableId, fieldDefaults, fieldCount))
            {
                _workspace.ShowToast("List is full");
            }

            UpdateComponentSnapshotManyAfter(payloadStart, components);
            CommitPendingEdit();
        }
    }

    public void RemovePrefabListItem(int widgetId, EntityId entity, ushort variableId, int itemIndex)
    {
        FlushPendingEdits();

        if (variableId == 0 || entity.IsNull || itemIndex < 0)
        {
            return;
        }

        UiNodeType nodeType = _workspace.World.GetNodeType(entity);
        if (nodeType == UiNodeType.Prefab)
        {
            if (!_workspace.World.TryGetComponent(entity, PrefabListDataComponent.Api.PoolIdConst, out AnyComponentHandle listAny) || !listAny.IsValid)
            {
                return;
            }

            uint stableId = _workspace.World.GetStableId(entity);
            if (stableId == 0)
            {
                return;
            }

            int payloadIndex = BeginOrUpdateComponentSnapshotEdit(widgetId, stableId, listAny);
            if (payloadIndex < 0)
            {
                return;
            }

            var listHandle = new PrefabListDataComponentHandle(listAny.Index, listAny.Generation);
            var list = PrefabListDataComponent.Api.FromHandle(_workspace.PropertyWorld, listHandle);
            if (list.IsAlive)
            {
                TryRemoveListItem(list, variableId, itemIndex);
            }

            UpdateComponentSnapshotAfter(payloadIndex, listAny);
            CommitPendingEdit();
            return;
        }

        if (nodeType == UiNodeType.PrefabInstance)
        {
            if (!_workspace.World.TryGetComponent(entity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) ||
                !instanceAny.IsValid)
            {
                return;
            }

            if (!_workspace.World.TryGetComponent(entity, PrefabListDataComponent.Api.PoolIdConst, out AnyComponentHandle listAny) || !listAny.IsValid)
            {
                return;
            }

            uint stableId = _workspace.World.GetStableId(entity);
            if (stableId == 0)
            {
                return;
            }

            var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var instance = PrefabInstanceComponent.Api.FromHandle(_workspace.PropertyWorld, instanceHandle);
            if (!instance.IsAlive)
            {
                return;
            }

            ushort count = instance.ValueCount;
            int n = Math.Min(count, (ushort)PrefabInstanceComponent.MaxVariables);
            ReadOnlySpan<ushort> ids = instance.VariableIdReadOnlySpan();

            int varIndex = FindIndex(ids, n, variableId);
            if (varIndex < 0)
            {
                return;
            }

            Span<AnyComponentHandle> components = stackalloc AnyComponentHandle[2] { instanceAny, listAny };
            int payloadStart = BeginOrUpdateComponentSnapshotManyEdit(widgetId, stableId, components);
            if (payloadStart < 0)
            {
                return;
            }

            var listHandle = new PrefabListDataComponentHandle(listAny.Index, listAny.Generation);
            var list = PrefabListDataComponent.Api.FromHandle(_workspace.PropertyWorld, listHandle);
            if (list.IsAlive)
            {
                EnsureInstanceListOverride(entity, instance, variableId, varIndex, ref list);
                instance.OverrideMask = instance.OverrideMask | (1UL << varIndex);
                TryRemoveListItem(list, variableId, itemIndex);
            }

            UpdateComponentSnapshotManyAfter(payloadStart, components);
            CommitPendingEdit();
        }
    }

    public void SetPrefabListItemField(int widgetId, bool isEditing, EntityId entity, ushort variableId, int itemIndex, int fieldIndex, PropertyKind fieldKind, PropertyValue value)
    {
        if (variableId == 0 || entity.IsNull || itemIndex < 0 || fieldIndex < 0)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        UiNodeType nodeType = _workspace.World.GetNodeType(entity);
        if (nodeType == UiNodeType.Prefab)
        {
            if (!_workspace.World.TryGetComponent(entity, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) || !varsAny.IsValid)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            if (!_workspace.World.TryGetComponent(entity, PrefabListDataComponent.Api.PoolIdConst, out AnyComponentHandle listAny) || !listAny.IsValid)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            uint stableId = _workspace.World.GetStableId(entity);
            if (stableId == 0)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            var listHandle = new PrefabListDataComponentHandle(listAny.Index, listAny.Generation);
            var list = PrefabListDataComponent.Api.FromHandle(_workspace.PropertyWorld, listHandle);
            if (!list.IsAlive)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
            var vars = PrefabVariablesComponent.Api.FromHandle(_workspace.PropertyWorld, varsHandle);
            if (!vars.IsAlive)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            ushort varCount = vars.VariableCount;
            int vn = Math.Min(varCount, (ushort)PrefabVariablesComponent.MaxVariables);
            ReadOnlySpan<ushort> varIds = vars.VariableIdReadOnlySpan();
            ReadOnlySpan<int> varKinds = vars.KindReadOnlySpan();
            ReadOnlySpan<PropertyValue> varDefaults = vars.DefaultValueReadOnlySpan();

            int listVarIndex = FindIndex(varIds, vn, variableId);
            if (listVarIndex < 0 || (PropertyKind)varKinds[listVarIndex] != PropertyKind.List)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            uint typeStableId = varDefaults[listVarIndex].UInt;
            if (typeStableId == 0)
            {
                _workspace.ShowToast("List type is missing");
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            Span<PropertyValue> fieldDefaults = stackalloc PropertyValue[PrefabListDataComponent.MaxFieldsPerItem];
            if (!TryGetListTypeFieldDefaults(typeStableId, fieldDefaults, out ushort fieldCount))
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            if (!TryEnsureListEntrySchema(list, variableId, fieldDefaults, fieldCount))
            {
                _workspace.ShowToast("List schema is invalid");
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            if (!TryGetListItemField(list, variableId, itemIndex, fieldIndex, out PropertyValue existing) ||
                existing.Equals(fieldKind, value))
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            int payloadIndex = BeginOrUpdateComponentSnapshotEdit(widgetId, stableId, listAny);
            TrySetListItemField(list, variableId, itemIndex, fieldIndex, value);
            UpdateComponentSnapshotAfter(payloadIndex, listAny);

            if (!isEditing)
            {
                CommitPendingEdit();
                return;
            }

            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        if (nodeType == UiNodeType.PrefabInstance)
        {
            if (!_workspace.World.TryGetComponent(entity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) ||
                !instanceAny.IsValid)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            if (!TryEnsurePrefabListDataComponent(entity, out AnyComponentHandle listAny) || !listAny.IsValid)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            uint stableId = _workspace.World.GetStableId(entity);
            if (stableId == 0)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var instance = PrefabInstanceComponent.Api.FromHandle(_workspace.PropertyWorld, instanceHandle);
            if (!instance.IsAlive)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            ushort count = instance.ValueCount;
            int n = Math.Min(count, (ushort)PrefabInstanceComponent.MaxVariables);
            ReadOnlySpan<ushort> ids = instance.VariableIdReadOnlySpan();
            int varIndex = FindIndex(ids, n, variableId);
            if (varIndex < 0)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            var listHandle = new PrefabListDataComponentHandle(listAny.Index, listAny.Generation);
            var list = PrefabListDataComponent.Api.FromHandle(_workspace.PropertyWorld, listHandle);
            if (!list.IsAlive)
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            EnsureInstanceListOverride(entity, instance, variableId, varIndex, ref list);

            uint typeStableId = instance.ValueReadOnlySpan()[varIndex].UInt;
            if (typeStableId == 0)
            {
                _workspace.ShowToast("List type is missing");
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            Span<PropertyValue> fieldDefaults = stackalloc PropertyValue[PrefabListDataComponent.MaxFieldsPerItem];
            if (!TryGetListTypeFieldDefaults(typeStableId, fieldDefaults, out ushort fieldCount))
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            if (!TryEnsureListEntrySchema(list, variableId, fieldDefaults, fieldCount))
            {
                _workspace.ShowToast("List schema is invalid");
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            if (!TryGetListItemField(list, variableId, itemIndex, fieldIndex, out PropertyValue existing) ||
                existing.Equals(fieldKind, value))
            {
                NotifyPropertyWidgetState(widgetId, isEditing);
                return;
            }

            Span<AnyComponentHandle> components = stackalloc AnyComponentHandle[2] { instanceAny, listAny };
            int payloadStart = BeginOrUpdateComponentSnapshotManyEdit(widgetId, stableId, components);
            instance.OverrideMask = instance.OverrideMask | (1UL << varIndex);
            TrySetListItemField(list, variableId, itemIndex, fieldIndex, value);
            UpdateComponentSnapshotManyAfter(payloadStart, components);

            if (!isEditing)
            {
                CommitPendingEdit();
                return;
            }

            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        NotifyPropertyWidgetState(widgetId, isEditing);
    }

    public void UpdatePrefabListSchema(int widgetId, EntityId entity, ushort variableId)
    {
        FlushPendingEdits();

        if (entity.IsNull || variableId == 0)
        {
            return;
        }

        UiNodeType nodeType = _workspace.World.GetNodeType(entity);
        if (nodeType == UiNodeType.Prefab)
        {
            if (!_workspace.World.TryGetComponent(entity, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) || !varsAny.IsValid)
            {
                return;
            }

            if (!_workspace.World.TryGetComponent(entity, PrefabListDataComponent.Api.PoolIdConst, out AnyComponentHandle listAny) || !listAny.IsValid)
            {
                return;
            }

            uint stableId = _workspace.World.GetStableId(entity);
            if (stableId == 0)
            {
                return;
            }

            var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
            var vars = PrefabVariablesComponent.Api.FromHandle(_workspace.PropertyWorld, varsHandle);
            if (!vars.IsAlive)
            {
                return;
            }

            ushort varCount = vars.VariableCount;
            int n = Math.Min(varCount, (ushort)PrefabVariablesComponent.MaxVariables);
            ReadOnlySpan<ushort> ids = vars.VariableIdReadOnlySpan();
            ReadOnlySpan<int> kinds = vars.KindReadOnlySpan();
            ReadOnlySpan<PropertyValue> defaults = vars.DefaultValueReadOnlySpan();

            int varIndex = FindIndex(ids, n, variableId);
            if (varIndex < 0 || (PropertyKind)kinds[varIndex] != PropertyKind.List)
            {
                return;
            }

            uint typeStableId = defaults[varIndex].UInt;
            if (typeStableId == 0)
            {
                _workspace.ShowToast("List type is missing");
                return;
            }

            Span<PropertyValue> fieldDefaults = stackalloc PropertyValue[PrefabListDataComponent.MaxFieldsPerItem];
            if (!TryGetListTypeFieldDefaults(typeStableId, fieldDefaults, out ushort fieldCount))
            {
                return;
            }

            int payloadIndex = BeginOrUpdateComponentSnapshotEdit(widgetId, stableId, listAny);
            if (payloadIndex < 0)
            {
                return;
            }

            var listHandle = new PrefabListDataComponentHandle(listAny.Index, listAny.Generation);
            var list = PrefabListDataComponent.Api.FromHandle(_workspace.PropertyWorld, listHandle);
            if (list.IsAlive)
            {
                if (!TryEnsureListEntrySchema(list, variableId, fieldDefaults, fieldCount))
                {
                    _workspace.ShowToast("List schema is invalid");
                }
            }

            UpdateComponentSnapshotAfter(payloadIndex, listAny);
            CommitPendingEdit();
            return;
        }

        if (nodeType == UiNodeType.PrefabInstance)
        {
            if (!_workspace.World.TryGetComponent(entity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) || !instanceAny.IsValid)
            {
                return;
            }

            if (!TryEnsurePrefabListDataComponent(entity, out AnyComponentHandle listAny) || !listAny.IsValid)
            {
                return;
            }

            uint stableId = _workspace.World.GetStableId(entity);
            if (stableId == 0)
            {
                return;
            }

            var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var instance = PrefabInstanceComponent.Api.FromHandle(_workspace.PropertyWorld, instanceHandle);
            if (!instance.IsAlive)
            {
                return;
            }

            ushort count = instance.ValueCount;
            int n = Math.Min(count, (ushort)PrefabInstanceComponent.MaxVariables);
            ReadOnlySpan<ushort> ids = instance.VariableIdReadOnlySpan();
            ReadOnlySpan<PropertyValue> values = instance.ValueReadOnlySpan();

            int varIndex = FindIndex(ids, n, variableId);
            if (varIndex < 0)
            {
                return;
            }

            uint typeStableId = values[varIndex].UInt;
            if (typeStableId == 0)
            {
                _workspace.ShowToast("List type is missing");
                return;
            }

            Span<PropertyValue> fieldDefaults = stackalloc PropertyValue[PrefabListDataComponent.MaxFieldsPerItem];
            if (!TryGetListTypeFieldDefaults(typeStableId, fieldDefaults, out ushort fieldCount))
            {
                return;
            }

            Span<AnyComponentHandle> components = stackalloc AnyComponentHandle[2] { instanceAny, listAny };
            int payloadStart = BeginOrUpdateComponentSnapshotManyEdit(widgetId, stableId, components);
            if (payloadStart < 0)
            {
                return;
            }

            var listHandle = new PrefabListDataComponentHandle(listAny.Index, listAny.Generation);
            var list = PrefabListDataComponent.Api.FromHandle(_workspace.PropertyWorld, listHandle);
            if (list.IsAlive)
            {
                EnsureInstanceListOverride(entity, instance, variableId, varIndex, ref list);
                instance.OverrideMask = instance.OverrideMask | (1UL << varIndex);

                if (!TryEnsureListEntrySchema(list, variableId, fieldDefaults, fieldCount))
                {
                    _workspace.ShowToast("List schema is invalid");
                }
            }

            UpdateComponentSnapshotManyAfter(payloadStart, components);
            CommitPendingEdit();
        }
    }

    private static int FindIndex(ReadOnlySpan<ushort> ids, int count, ushort id)
    {
        int n = Math.Min(count, ids.Length);
        for (int i = 0; i < n; i++)
        {
            if (ids[i] == id)
            {
                return i;
            }
        }

        return -1;
    }

    private bool TryGetListTypeFieldDefaults(uint typePrefabStableId, Span<PropertyValue> fieldDefaults, out ushort fieldCount)
    {
        fieldCount = 0;

        EntityId typePrefabEntity = _workspace.World.GetEntityByStableId(typePrefabStableId);
        if (typePrefabEntity.IsNull || _workspace.World.GetNodeType(typePrefabEntity) != UiNodeType.Prefab)
        {
            _workspace.ShowToast("Invalid list type prefab");
            return false;
        }

        if (!_workspace.World.TryGetComponent(typePrefabEntity, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) || !varsAny.IsValid)
        {
            return true;
        }

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        var vars = PrefabVariablesComponent.Api.FromHandle(_workspace.PropertyWorld, varsHandle);
        if (!vars.IsAlive || vars.VariableCount == 0)
        {
            return true;
        }

        ushort count = vars.VariableCount;
        int n = Math.Min(count, (ushort)PrefabVariablesComponent.MaxVariables);
        ReadOnlySpan<ushort> ids = vars.VariableIdReadOnlySpan();
        ReadOnlySpan<int> kinds = vars.KindReadOnlySpan();
        ReadOnlySpan<PropertyValue> defaults = vars.DefaultValueReadOnlySpan();

        ushort outCount = 0;
        for (int i = 0; i < n && outCount < PrefabListDataComponent.MaxFieldsPerItem; i++)
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

            fieldDefaults[outCount++] = defaults[i];
        }

        fieldCount = outCount;
        return true;
    }

    private static bool TryGetListItemField(PrefabListDataComponent.ViewProxy list, ushort variableId, int itemIndex, int fieldIndex, out PropertyValue value)
    {
        value = default;

        ushort entryCount = list.EntryCount;
        int n = System.Math.Min((int)entryCount, PrefabListDataComponent.MaxListEntries);
        ReadOnlySpan<ushort> entryVarId = list.EntryVariableIdReadOnlySpan();
        ReadOnlySpan<ushort> entryItemCount = list.EntryItemCountReadOnlySpan();
        ReadOnlySpan<ushort> entryItemStart = list.EntryItemStartReadOnlySpan();
        ReadOnlySpan<ushort> entryFieldCount = list.EntryFieldCountReadOnlySpan();

        int entryIndex = FindIndex(entryVarId, n, variableId);
        if (entryIndex < 0)
        {
            return false;
        }

        int items = entryItemCount[entryIndex];
        int fields = entryFieldCount[entryIndex];
        if ((uint)itemIndex >= (uint)items || (uint)fieldIndex >= (uint)fields)
        {
            return false;
        }

        int start = entryItemStart[entryIndex];
        int index = start + itemIndex * fields + fieldIndex;
        if ((uint)index >= (uint)PrefabListDataComponent.MaxTotalSlots)
        {
            return false;
        }

        value = list.ItemsReadOnlySpan()[index];
        return true;
    }

    private static bool TrySetListItemField(PrefabListDataComponent.ViewProxy list, ushort variableId, int itemIndex, int fieldIndex, PropertyValue value)
    {
        ushort entryCount = list.EntryCount;
        int n = System.Math.Min((int)entryCount, PrefabListDataComponent.MaxListEntries);
        ReadOnlySpan<ushort> entryVarId = list.EntryVariableIdReadOnlySpan();
        ReadOnlySpan<ushort> entryItemCount = list.EntryItemCountReadOnlySpan();
        ReadOnlySpan<ushort> entryItemStart = list.EntryItemStartReadOnlySpan();
        ReadOnlySpan<ushort> entryFieldCount = list.EntryFieldCountReadOnlySpan();

        int entryIndex = FindIndex(entryVarId, n, variableId);
        if (entryIndex < 0)
        {
            return false;
        }

        int items = entryItemCount[entryIndex];
        int fields = entryFieldCount[entryIndex];
        if ((uint)itemIndex >= (uint)items || (uint)fieldIndex >= (uint)fields)
        {
            return false;
        }

        int start = entryItemStart[entryIndex];
        int index = start + itemIndex * fields + fieldIndex;
        if ((uint)index >= (uint)PrefabListDataComponent.MaxTotalSlots)
        {
            return false;
        }

        list.ItemsSpan()[index] = value;
        return true;
    }

    private static void RemoveListEntryIfExists(PrefabListDataComponent.ViewProxy list, ushort variableId)
    {
        ushort entryCount = list.EntryCount;
        int n = System.Math.Min((int)entryCount, PrefabListDataComponent.MaxListEntries);
        Span<ushort> entryVarId = list.EntryVariableIdSpan();
        Span<ushort> entryItemCount = list.EntryItemCountSpan();
        Span<ushort> entryItemStart = list.EntryItemStartSpan();
        Span<ushort> entryFieldCount = list.EntryFieldCountSpan();
        Span<PropertyValue> items = list.ItemsSpan();

        int entryIndex = FindIndex(entryVarId, n, variableId);
        if (entryIndex < 0)
        {
            return;
        }

        int removeCount = entryItemCount[entryIndex] * entryFieldCount[entryIndex];
        int removeStart = entryItemStart[entryIndex];

        int usedEnd = GetUsedEnd(entryItemStart, entryItemCount, entryFieldCount, n);

        if (removeCount > 0 && usedEnd > removeStart)
        {
            int tailStart = removeStart + removeCount;
            int tailCount = usedEnd - tailStart;
            for (int i = 0; i < tailCount; i++)
            {
                items[removeStart + i] = items[tailStart + i];
            }

            for (int i = usedEnd - removeCount; i < usedEnd; i++)
            {
                items[i] = default;
            }

            for (int e = entryIndex + 1; e < n; e++)
            {
                entryItemStart[e] = (ushort)(entryItemStart[e] - removeCount);
            }
        }

        for (int e = entryIndex; e < n - 1; e++)
        {
            entryVarId[e] = entryVarId[e + 1];
            entryItemCount[e] = entryItemCount[e + 1];
            entryItemStart[e] = entryItemStart[e + 1];
            entryFieldCount[e] = entryFieldCount[e + 1];
        }

        int last = n - 1;
        entryVarId[last] = 0;
        entryItemCount[last] = 0;
        entryItemStart[last] = 0;
        entryFieldCount[last] = 0;
        list.EntryCount = (ushort)(n - 1);
    }

    private static void ResetListEntry(PrefabListDataComponent.ViewProxy list, ushort variableId, ushort fieldCount)
    {
        if (fieldCount > PrefabListDataComponent.MaxFieldsPerItem)
        {
            fieldCount = PrefabListDataComponent.MaxFieldsPerItem;
        }

        ushort entryCount = list.EntryCount;
        int n = System.Math.Min((int)entryCount, PrefabListDataComponent.MaxListEntries);
        Span<ushort> entryVarId = list.EntryVariableIdSpan();
        Span<ushort> entryItemCount = list.EntryItemCountSpan();
        Span<ushort> entryItemStart = list.EntryItemStartSpan();
        Span<ushort> entryFieldCount = list.EntryFieldCountSpan();
        Span<PropertyValue> items = list.ItemsSpan();

        int entryIndex = FindIndex(entryVarId, n, variableId);
        if (entryIndex < 0)
        {
            if (n >= PrefabListDataComponent.MaxListEntries)
            {
                return;
            }

            int usedEnd = GetUsedEnd(entryItemStart, entryItemCount, entryFieldCount, n);
            entryIndex = n;
            entryVarId[entryIndex] = variableId;
            entryItemCount[entryIndex] = 0;
            entryItemStart[entryIndex] = (ushort)usedEnd;
            entryFieldCount[entryIndex] = fieldCount;
            list.EntryCount = (ushort)(n + 1);
            return;
        }

        int oldCount = entryItemCount[entryIndex];
        int oldFields = entryFieldCount[entryIndex];
        int removeCount = oldCount * oldFields;
        int removeStart = entryItemStart[entryIndex];
        int usedEndOld = GetUsedEnd(entryItemStart, entryItemCount, entryFieldCount, n);

        if (removeCount > 0 && usedEndOld > removeStart)
        {
            int tailStart = removeStart + removeCount;
            int tailCount = usedEndOld - tailStart;
            for (int i = 0; i < tailCount; i++)
            {
                items[removeStart + i] = items[tailStart + i];
            }

            for (int i = usedEndOld - removeCount; i < usedEndOld; i++)
            {
                items[i] = default;
            }

            for (int e = entryIndex + 1; e < n; e++)
            {
                entryItemStart[e] = (ushort)(entryItemStart[e] - removeCount);
            }
        }

        entryItemCount[entryIndex] = 0;
        entryFieldCount[entryIndex] = fieldCount;
    }

    private static bool TryAppendListItem(PrefabListDataComponent.ViewProxy list, ushort variableId, ReadOnlySpan<PropertyValue> fieldDefaults, ushort fieldCount)
    {
        if (fieldCount == 0)
        {
            // Allow empty schema: still increment item count up to max.
            fieldCount = 0;
        }
        else if (fieldCount > PrefabListDataComponent.MaxFieldsPerItem)
        {
            fieldCount = PrefabListDataComponent.MaxFieldsPerItem;
        }

        ushort entryCount = list.EntryCount;
        int n = System.Math.Min((int)entryCount, PrefabListDataComponent.MaxListEntries);
        Span<ushort> entryVarId = list.EntryVariableIdSpan();
        Span<ushort> entryItemCount = list.EntryItemCountSpan();
        Span<ushort> entryItemStart = list.EntryItemStartSpan();
        Span<ushort> entryFieldCount = list.EntryFieldCountSpan();
        Span<PropertyValue> items = list.ItemsSpan();

        int entryIndex = FindIndex(entryVarId, n, variableId);
        if (entryIndex < 0)
        {
            if (n >= PrefabListDataComponent.MaxListEntries)
            {
                return false;
            }

            int usedEnd = GetUsedEnd(entryItemStart, entryItemCount, entryFieldCount, n);
            entryIndex = n;
            entryVarId[entryIndex] = variableId;
            entryItemCount[entryIndex] = 0;
            entryItemStart[entryIndex] = (ushort)usedEnd;
            entryFieldCount[entryIndex] = fieldCount;
            list.EntryCount = (ushort)(n + 1);
            n++;
        }

        int currentFields = entryFieldCount[entryIndex];
        if (currentFields == 0)
        {
            entryFieldCount[entryIndex] = fieldCount;
            currentFields = fieldCount;
        }
        if (currentFields != fieldCount)
        {
            // Schema mismatch: refuse to append.
            return false;
        }

        int currentItems = entryItemCount[entryIndex];
        if (currentItems >= PrefabListDataComponent.MaxItemsPerList)
        {
            return false;
        }

        int usedEnd2 = GetUsedEnd(entryItemStart, entryItemCount, entryFieldCount, n);
        int insertCount = currentFields;
        int insertAt = entryItemStart[entryIndex] + currentItems * currentFields;

        if (insertCount > 0)
        {
            if (usedEnd2 + insertCount > PrefabListDataComponent.MaxTotalSlots)
            {
                return false;
            }

            for (int i = usedEnd2 - 1; i >= insertAt; i--)
            {
                items[i + insertCount] = items[i];
            }

            for (int e = entryIndex + 1; e < n; e++)
            {
                entryItemStart[e] = (ushort)(entryItemStart[e] + insertCount);
            }

            for (int f = 0; f < insertCount; f++)
            {
                items[insertAt + f] = fieldDefaults[f];
            }
        }

        entryItemCount[entryIndex] = (ushort)(currentItems + 1);
        return true;
    }

    private static bool TryEnsureListEntrySchema(
        PrefabListDataComponent.ViewProxy list,
        ushort variableId,
        ReadOnlySpan<PropertyValue> fieldDefaults,
        ushort fieldCount)
    {
        if (fieldCount > PrefabListDataComponent.MaxFieldsPerItem)
        {
            fieldCount = PrefabListDataComponent.MaxFieldsPerItem;
        }
        if (fieldDefaults.Length < fieldCount)
        {
            fieldCount = (ushort)fieldDefaults.Length;
        }

        ushort entryCount = list.EntryCount;
        int n = System.Math.Min((int)entryCount, PrefabListDataComponent.MaxListEntries);
        Span<ushort> entryVarId = list.EntryVariableIdSpan();
        Span<ushort> entryItemCount = list.EntryItemCountSpan();
        Span<ushort> entryItemStart = list.EntryItemStartSpan();
        Span<ushort> entryFieldCount = list.EntryFieldCountSpan();
        Span<PropertyValue> items = list.ItemsSpan();

        int entryIndex = FindIndex(entryVarId, n, variableId);
        if (entryIndex < 0)
        {
            if (n >= PrefabListDataComponent.MaxListEntries)
            {
                return false;
            }

            int usedEnd = GetUsedEnd(entryItemStart, entryItemCount, entryFieldCount, n);
            entryIndex = n;
            entryVarId[entryIndex] = variableId;
            entryItemCount[entryIndex] = 0;
            entryItemStart[entryIndex] = (ushort)usedEnd;
            entryFieldCount[entryIndex] = fieldCount;
            list.EntryCount = (ushort)(n + 1);
            return true;
        }

        ushort oldFieldCount = entryFieldCount[entryIndex];
        if (oldFieldCount == fieldCount)
        {
            return true;
        }

        int itemCount = entryItemCount[entryIndex];
        int start = entryItemStart[entryIndex];
        int oldSlots = itemCount * oldFieldCount;
        int newSlots = itemCount * fieldCount;

        if (oldSlots > PrefabListDataComponent.MaxTotalSlots || newSlots > PrefabListDataComponent.MaxTotalSlots)
        {
            return false;
        }

        Span<PropertyValue> oldCopy = stackalloc PropertyValue[PrefabListDataComponent.MaxTotalSlots];
        for (int i = 0; i < oldSlots; i++)
        {
            int idx = start + i;
            if ((uint)idx >= (uint)items.Length)
            {
                break;
            }
            oldCopy[i] = items[idx];
        }

        int usedEndOld = GetUsedEnd(entryItemStart, entryItemCount, entryFieldCount, n);
        int delta = newSlots - oldSlots;

        if (delta > 0)
        {
            if (usedEndOld + delta > PrefabListDataComponent.MaxTotalSlots)
            {
                return false;
            }

            int tailStart = start + oldSlots;
            for (int i = usedEndOld - 1; i >= tailStart; i--)
            {
                items[i + delta] = items[i];
            }
        }
        else if (delta < 0)
        {
            int tailStart = start + oldSlots;
            int newTailStart = start + newSlots;
            int tailCount = usedEndOld - tailStart;
            if (tailCount > 0)
            {
                for (int i = 0; i < tailCount; i++)
                {
                    items[newTailStart + i] = items[tailStart + i];
                }
            }

            int usedEndNew = usedEndOld + delta;
            for (int i = usedEndNew; i < usedEndOld; i++)
            {
                if ((uint)i < (uint)items.Length)
                {
                    items[i] = default;
                }
            }
        }

        if (delta != 0)
        {
            for (int e = entryIndex + 1; e < n; e++)
            {
                entryItemStart[e] = (ushort)(entryItemStart[e] + delta);
            }
        }

        entryFieldCount[entryIndex] = fieldCount;

        int minFields = oldFieldCount;
        if (fieldCount < minFields)
        {
            minFields = fieldCount;
        }

        for (int itemIndex = 0; itemIndex < itemCount; itemIndex++)
        {
            int oldBase = itemIndex * oldFieldCount;
            int newBase = itemIndex * fieldCount;

            for (int f = 0; f < minFields; f++)
            {
                items[start + newBase + f] = oldCopy[oldBase + f];
            }

            for (int f = minFields; f < fieldCount; f++)
            {
                items[start + newBase + f] = fieldDefaults[f];
            }
        }

        return true;
    }

    private static bool TryRemoveListItem(PrefabListDataComponent.ViewProxy list, ushort variableId, int itemIndex)
    {
        ushort entryCount = list.EntryCount;
        int n = System.Math.Min((int)entryCount, PrefabListDataComponent.MaxListEntries);
        Span<ushort> entryVarId = list.EntryVariableIdSpan();
        Span<ushort> entryItemCount = list.EntryItemCountSpan();
        Span<ushort> entryItemStart = list.EntryItemStartSpan();
        Span<ushort> entryFieldCount = list.EntryFieldCountSpan();
        Span<PropertyValue> items = list.ItemsSpan();

        int entryIndex = FindIndex(entryVarId, n, variableId);
        if (entryIndex < 0)
        {
            return false;
        }

        int fields = entryFieldCount[entryIndex];
        int count = entryItemCount[entryIndex];
        if ((uint)itemIndex >= (uint)count)
        {
            return false;
        }

        int removeCount = fields;
        int removeStart = entryItemStart[entryIndex] + itemIndex * fields;
        int usedEnd = GetUsedEnd(entryItemStart, entryItemCount, entryFieldCount, n);

        if (removeCount > 0 && usedEnd > removeStart)
        {
            int tailStart = removeStart + removeCount;
            int tailCount = usedEnd - tailStart;
            for (int i = 0; i < tailCount; i++)
            {
                items[removeStart + i] = items[tailStart + i];
            }

            for (int i = usedEnd - removeCount; i < usedEnd; i++)
            {
                items[i] = default;
            }

            for (int e = entryIndex + 1; e < n; e++)
            {
                entryItemStart[e] = (ushort)(entryItemStart[e] - removeCount);
            }
        }

        entryItemCount[entryIndex] = (ushort)(count - 1);
        return true;
    }

    private static int GetUsedEnd(ReadOnlySpan<ushort> entryItemStart, ReadOnlySpan<ushort> entryItemCount, ReadOnlySpan<ushort> entryFieldCount, int entryCount)
    {
        int usedEnd = 0;
        for (int e = 0; e < entryCount; e++)
        {
            int start = entryItemStart[e];
            int count = entryItemCount[e];
            int fields = entryFieldCount[e];
            int end = start + count * fields;
            if (end > usedEnd)
            {
                usedEnd = end;
            }
        }

        if (usedEnd < 0)
        {
            usedEnd = 0;
        }
        else if (usedEnd > PrefabListDataComponent.MaxTotalSlots)
        {
            usedEnd = PrefabListDataComponent.MaxTotalSlots;
        }

        return usedEnd;
    }

    private void EnsureInstanceListOverride(EntityId instanceEntity, PrefabInstanceComponent.ViewProxy instance, ushort variableId, int variableIndex, ref PrefabListDataComponent.ViewProxy list)
    {
        if (instanceEntity.IsNull || variableId == 0 || variableIndex < 0)
        {
            return;
        }

        ushort entryCount = list.EntryCount;
        int n = System.Math.Min((int)entryCount, PrefabListDataComponent.MaxListEntries);
        ReadOnlySpan<ushort> entryVarId = list.EntryVariableIdReadOnlySpan();
        if (FindIndex(entryVarId, n, variableId) >= 0)
        {
            return;
        }

        uint sourcePrefabStableId = instance.SourcePrefabStableId;
        if (sourcePrefabStableId == 0)
        {
            return;
        }

        EntityId sourcePrefabEntity = _workspace.World.GetEntityByStableId(sourcePrefabStableId);
        if (sourcePrefabEntity.IsNull || _workspace.World.GetNodeType(sourcePrefabEntity) != UiNodeType.Prefab)
        {
            return;
        }

        if (!_workspace.World.TryGetComponent(sourcePrefabEntity, PrefabListDataComponent.Api.PoolIdConst, out AnyComponentHandle srcListAny) || !srcListAny.IsValid)
        {
            // No defaults: create empty entry at end using field count inferred from type (if present).
            uint typeStableId = 0;
            ushort valueCount = instance.ValueCount;
            int vn = Math.Min(valueCount, (ushort)PrefabInstanceComponent.MaxVariables);
            ReadOnlySpan<ushort> ids = instance.VariableIdReadOnlySpan();
            ReadOnlySpan<PropertyValue> values = instance.ValueReadOnlySpan();
            int idx = FindIndex(ids, vn, variableId);
            if (idx >= 0)
            {
                typeStableId = values[idx].UInt;
            }

            Span<PropertyValue> defaults = stackalloc PropertyValue[PrefabListDataComponent.MaxFieldsPerItem];
            ushort fields = 0;
            if (typeStableId != 0)
            {
                TryGetListTypeFieldDefaults(typeStableId, defaults, out fields);
            }

            ResetListEntry(list, variableId, fields);
            return;
        }

        var srcHandle = new PrefabListDataComponentHandle(srcListAny.Index, srcListAny.Generation);
        var srcList = PrefabListDataComponent.Api.FromHandle(_workspace.PropertyWorld, srcHandle);
        if (!srcList.IsAlive)
        {
            return;
        }

        ushort srcEntryCount = srcList.EntryCount;
        int sn = System.Math.Min((int)srcEntryCount, PrefabListDataComponent.MaxListEntries);
        ReadOnlySpan<ushort> srcEntryVarId = srcList.EntryVariableIdReadOnlySpan();
        ReadOnlySpan<ushort> srcItemCount = srcList.EntryItemCountReadOnlySpan();
        ReadOnlySpan<ushort> srcItemStart = srcList.EntryItemStartReadOnlySpan();
        ReadOnlySpan<ushort> srcFieldCount = srcList.EntryFieldCountReadOnlySpan();
        ReadOnlySpan<PropertyValue> srcItems = srcList.ItemsReadOnlySpan();

        int srcEntryIndex = FindIndex(srcEntryVarId, sn, variableId);
        if (srcEntryIndex < 0)
        {
            ResetListEntry(list, variableId, fieldCount: 0);
            return;
        }

        ushort fieldsPerItem = srcFieldCount[srcEntryIndex];
        int copyCount = srcItemCount[srcEntryIndex] * fieldsPerItem;
        ResetListEntry(list, variableId, fieldsPerItem);

        if (copyCount <= 0)
        {
            return;
        }

        // Copy items after reset (entry start is stable, but other entries may have shifted).
        ushort entryCount2 = list.EntryCount;
        int n2 = System.Math.Min((int)entryCount2, PrefabListDataComponent.MaxListEntries);
        ReadOnlySpan<ushort> dstEntryVarId = list.EntryVariableIdReadOnlySpan();
        ReadOnlySpan<ushort> dstItemStart = list.EntryItemStartReadOnlySpan();
        Span<ushort> dstItemCount = list.EntryItemCountSpan();
        Span<PropertyValue> dstItems = list.ItemsSpan();

        int dstEntryIndex = FindIndex(dstEntryVarId, n2, variableId);
        if (dstEntryIndex < 0)
        {
            return;
        }

        int usedEnd = GetUsedEnd(list.EntryItemStartReadOnlySpan(), list.EntryItemCountReadOnlySpan(), list.EntryFieldCountReadOnlySpan(), n2);
        int dstStart = dstItemStart[dstEntryIndex];
        int insertAt = dstStart;

        if (usedEnd + copyCount > PrefabListDataComponent.MaxTotalSlots)
        {
            return;
        }

        // Insert copyCount slots at dstStart (entry is currently zero-sized).
        for (int i = usedEnd - 1; i >= insertAt; i--)
        {
            dstItems[i + copyCount] = dstItems[i];
        }

        Span<ushort> dstStarts = list.EntryItemStartSpan();
        for (int e = dstEntryIndex + 1; e < n2; e++)
        {
            dstStarts[e] = (ushort)(dstStarts[e] + copyCount);
        }

        int srcStart = srcItemStart[srcEntryIndex];
        for (int i = 0; i < copyCount; i++)
        {
            dstItems[dstStart + i] = srcItems[srcStart + i];
        }

        dstItemCount[dstEntryIndex] = srcItemCount[srcEntryIndex];
    }

    private void AutoKeyPrefabInstanceVariableEdited(EntityId instanceEntity, ushort variableId, PropertyKind kind, in PropertyValue newValue)
    {
        if (!_workspace.TryGetActiveAnimationDocument(out var animations) || !animations.TryGetSelectedTimeline(out var timeline) || timeline == null)
        {
            return;
        }

        if (instanceEntity.IsNull)
        {
            return;
        }

        uint stableId = _workspace.World.GetStableId(instanceEntity);
        if (stableId == 0)
        {
            return;
        }

        bool canCreateKeys = AnimationEditorWindow.IsAutoKeyEnabled();
        int frame = Math.Clamp(AnimationEditorWindow.GetCurrentFrame(), 0, Math.Max(1, timeline.DurationFrames));

        bool hasTarget = AnimationEditorHelpers.TryGetTargetIndex(timeline, stableId, out int targetIndex);
        if (!hasTarget)
        {
            if (!canCreateKeys)
            {
                return;
            }

            targetIndex = AnimationEditorHelpers.GetOrAddTargetIndex(timeline, stableId);
            if (targetIndex < 0)
            {
                return;
            }
        }

        var binding = new AnimationDocument.AnimationBinding(
            targetIndex: targetIndex,
            componentKind: PrefabInstanceComponent.Api.PoolIdConst,
            propertyIndexHint: 0,
            propertyId: AnimationBindingIds.MakePrefabVariablePropertyId(variableId),
            propertyKind: kind);

        var directTrack = AnimationEditorHelpers.FindTrack(timeline, binding);
        if (directTrack != null && TrySetKeyValueAtFrame(directTrack, frame, newValue))
        {
            return;
        }

        if (canCreateKeys)
        {
            AnimationEditorWindow.AutoKeyAtCurrentFrame(_workspace, timeline, binding, selectKeyframe: false);
        }
    }

    public bool TryGetKeyablePrefabInstanceVariableState(EntityId instanceEntity, ushort variableId, PropertyKind kind, out bool hasTrack, out bool hasKeyAtPlayhead)
    {
        hasTrack = false;
        hasKeyAtPlayhead = false;

        if (variableId == 0 || instanceEntity.IsNull)
        {
            return false;
        }

        if (!_workspace.TryGetActiveAnimationDocument(out var animations) || !animations.TryGetSelectedTimeline(out var timeline) || timeline == null)
        {
            return false;
        }

        uint stableId = _workspace.World.GetStableId(instanceEntity);
        if (stableId == 0)
        {
            return false;
        }

        int frame = Math.Clamp(AnimationEditorWindow.GetCurrentFrame(), 0, Math.Max(1, timeline.DurationFrames));
        if (!AnimationEditorHelpers.TryGetTargetIndex(timeline, stableId, out int targetIndex))
        {
            return true;
        }

        var binding = new AnimationDocument.AnimationBinding(
            targetIndex: targetIndex,
            componentKind: PrefabInstanceComponent.Api.PoolIdConst,
            propertyIndexHint: 0,
            propertyId: AnimationBindingIds.MakePrefabVariablePropertyId(variableId),
            propertyKind: kind);

        var track = AnimationEditorHelpers.FindTrack(timeline, binding);
        hasTrack = track != null;
        hasKeyAtPlayhead = track != null && AnimationEditorHelpers.HasKeyAtFrame(timeline, binding, frame);
        return true;
    }

    public void AddPrefabInstanceVariableKeyAtPlayhead(EntityId instanceEntity, ushort variableId, PropertyKind kind)
    {
        if (variableId == 0 || instanceEntity.IsNull)
        {
            return;
        }

        if (!_workspace.TryGetActiveAnimationDocument(out var animations) || !animations.TryGetSelectedTimeline(out var timeline) || timeline == null)
        {
            return;
        }

        uint stableId = _workspace.World.GetStableId(instanceEntity);
        if (stableId == 0)
        {
            return;
        }

        int targetIndex = AnimationEditorHelpers.GetOrAddTargetIndex(timeline, stableId);
        if (targetIndex < 0)
        {
            return;
        }

        var binding = new AnimationDocument.AnimationBinding(
            targetIndex: targetIndex,
            componentKind: PrefabInstanceComponent.Api.PoolIdConst,
            propertyIndexHint: 0,
            propertyId: AnimationBindingIds.MakePrefabVariablePropertyId(variableId),
            propertyKind: kind);

        AnimationEditorWindow.AutoKeyAtCurrentFrame(_workspace, timeline, binding, selectKeyframe: false);
    }

    public bool SetPrefabPropertyBinding(EntityId prefabEntity, uint targetSourceNodeStableId, ushort targetComponentKind, in PropertySlot slot, ushort variableId, byte direction)
    {
        FlushPendingEdits();

        if (prefabEntity.IsNull || _workspace.World.GetNodeType(prefabEntity) != UiNodeType.Prefab)
        {
            return false;
        }

        if (targetSourceNodeStableId == 0 || targetComponentKind == 0 || slot.PropertyId == 0)
        {
            return false;
        }

        if (!TryEnsurePrefabBindingsComponent(prefabEntity, out AnyComponentHandle bindingsAny))
        {
            return false;
        }

        uint prefabStableId = _workspace.World.GetStableId(prefabEntity);
        if (prefabStableId == 0)
        {
            return false;
        }

        int size = UiComponentClipboardPacker.GetSnapshotSize(PrefabBindingsComponent.Api.PoolIdConst);
        if (size <= 0)
        {
            return false;
        }

        var beforeBytes = new byte[size];
        UiComponentClipboardPacker.TryPack(_workspace.PropertyWorld, bindingsAny, beforeBytes);

        var handle = new PrefabBindingsComponentHandle(bindingsAny.Index, bindingsAny.Generation);
        var bindings = PrefabBindingsComponent.Api.FromHandle(_workspace.PropertyWorld, handle);
        if (!bindings.IsAlive)
        {
            return false;
        }

        ushort count = bindings.BindingCount;
        if (count > PrefabBindingsComponent.MaxBindings)
        {
            count = PrefabBindingsComponent.MaxBindings;
        }

        Span<ushort> bindVarId = bindings.VariableIdSpan();
        Span<byte> bindDirection = bindings.DirectionSpan();
        Span<uint> bindTargetSourceNode = bindings.TargetSourceNodeStableIdSpan();
        Span<ushort> bindTargetComponentKind = bindings.TargetComponentKindSpan();
        Span<ushort> bindPropertyIndexHint = bindings.PropertyIndexHintSpan();
        Span<ulong> bindPropertyId = bindings.PropertyIdSpan();
        Span<ushort> bindPropertyKind = bindings.PropertyKindSpan();

        int existingIndex = -1;
        for (int i = 0; i < count; i++)
        {
            if (bindTargetSourceNode[i] == targetSourceNodeStableId &&
                bindTargetComponentKind[i] == targetComponentKind &&
                bindPropertyId[i] == slot.PropertyId)
            {
                existingIndex = i;
                break;
            }
        }

        bool changed = false;

        if (variableId == 0)
        {
            if (existingIndex >= 0)
            {
                int last = count - 1;
                if (existingIndex != last)
                {
                    bindVarId[existingIndex] = bindVarId[last];
                    bindDirection[existingIndex] = bindDirection[last];
                    bindTargetSourceNode[existingIndex] = bindTargetSourceNode[last];
                    bindTargetComponentKind[existingIndex] = bindTargetComponentKind[last];
                    bindPropertyIndexHint[existingIndex] = bindPropertyIndexHint[last];
                    bindPropertyId[existingIndex] = bindPropertyId[last];
                    bindPropertyKind[existingIndex] = bindPropertyKind[last];
                }

                bindVarId[last] = 0;
                bindDirection[last] = 0;
                bindTargetSourceNode[last] = 0;
                bindTargetComponentKind[last] = 0;
                bindPropertyIndexHint[last] = 0;
                bindPropertyId[last] = 0;
                bindPropertyKind[last] = 0;

                bindings.BindingCount = (ushort)(count - 1);
                changed = true;
            }
        }
        else if (existingIndex >= 0)
        {
            if (bindVarId[existingIndex] != variableId || bindDirection[existingIndex] != direction)
            {
                bindVarId[existingIndex] = variableId;
                bindDirection[existingIndex] = direction;
                bindTargetSourceNode[existingIndex] = targetSourceNodeStableId;
                bindTargetComponentKind[existingIndex] = targetComponentKind;
                int propertyIndex = slot.PropertyIndex;
                if (propertyIndex < 0)
                {
                    propertyIndex = 0;
                }
                if (propertyIndex > ushort.MaxValue)
                {
                    propertyIndex = ushort.MaxValue;
                }
                bindPropertyIndexHint[existingIndex] = (ushort)propertyIndex;
                bindPropertyId[existingIndex] = slot.PropertyId;
                bindPropertyKind[existingIndex] = (ushort)slot.Kind;
                changed = true;
            }
        }
        else
        {
            if (count >= PrefabBindingsComponent.MaxBindings)
            {
                _workspace.ShowToast("Max bindings reached");
                return false;
            }

            int i = count;
            bindVarId[i] = variableId;
            bindDirection[i] = direction;
            bindTargetSourceNode[i] = targetSourceNodeStableId;
            bindTargetComponentKind[i] = targetComponentKind;
            int propertyIndex = slot.PropertyIndex;
            if (propertyIndex < 0)
            {
                propertyIndex = 0;
            }
            if (propertyIndex > ushort.MaxValue)
            {
                propertyIndex = ushort.MaxValue;
            }
            bindPropertyIndexHint[i] = (ushort)propertyIndex;
            bindPropertyId[i] = slot.PropertyId;
            bindPropertyKind[i] = (ushort)slot.Kind;
            bindings.BindingCount = (ushort)(count + 1);
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        bindings.Revision = bindings.Revision + 1;

        var afterBytes = new byte[size];
        UiComponentClipboardPacker.TryPack(_workspace.PropertyWorld, bindingsAny, afterBytes);

        int payloadIndex = _undoComponentSnapshotPayload.Count;
        _undoComponentSnapshotPayload.Add(new ComponentSnapshotUndoPayload
        {
            EntityStableId = prefabStableId,
            ComponentKind = PrefabBindingsComponent.Api.PoolIdConst,
            Before = beforeBytes,
            After = afterBytes
        });
        _undoStack.Push(UndoRecord.ForComponentSnapshot(payloadIndex));
        return true;
    }

    public bool SetPrefabListLayoutBinding(EntityId prefabEntity, uint targetSourceNodeStableId, ushort variableId)
    {
        FlushPendingEdits();

        if (prefabEntity.IsNull || _workspace.World.GetNodeType(prefabEntity) != UiNodeType.Prefab)
        {
            return false;
        }

        if (targetSourceNodeStableId == 0)
        {
            return false;
        }

        if (!TryEnsurePrefabBindingsComponent(prefabEntity, out AnyComponentHandle bindingsAny))
        {
            return false;
        }

        uint prefabStableId = _workspace.World.GetStableId(prefabEntity);
        if (prefabStableId == 0)
        {
            return false;
        }

        int size = UiComponentClipboardPacker.GetSnapshotSize(PrefabBindingsComponent.Api.PoolIdConst);
        if (size <= 0)
        {
            return false;
        }

        var beforeBytes = new byte[size];
        UiComponentClipboardPacker.TryPack(_workspace.PropertyWorld, bindingsAny, beforeBytes);

        var handle = new PrefabBindingsComponentHandle(bindingsAny.Index, bindingsAny.Generation);
        var bindings = PrefabBindingsComponent.Api.FromHandle(_workspace.PropertyWorld, handle);
        if (!bindings.IsAlive)
        {
            return false;
        }

        const byte DirectionListBind = 2;

        ushort count = bindings.BindingCount;
        if (count > PrefabBindingsComponent.MaxBindings)
        {
            count = PrefabBindingsComponent.MaxBindings;
        }

        Span<ushort> bindVarId = bindings.VariableIdSpan();
        Span<byte> bindDirection = bindings.DirectionSpan();
        Span<uint> bindTargetSourceNode = bindings.TargetSourceNodeStableIdSpan();
        Span<ushort> bindTargetComponentKind = bindings.TargetComponentKindSpan();
        Span<ushort> bindPropertyIndexHint = bindings.PropertyIndexHintSpan();
        Span<ulong> bindPropertyId = bindings.PropertyIdSpan();
        Span<ushort> bindPropertyKind = bindings.PropertyKindSpan();

        int existingIndex = -1;
        for (int i = 0; i < count; i++)
        {
            if (bindDirection[i] == DirectionListBind && bindTargetSourceNode[i] == targetSourceNodeStableId)
            {
                existingIndex = i;
                break;
            }
        }

        bool changed = false;

        if (variableId == 0)
        {
            if (existingIndex >= 0)
            {
                int last = count - 1;
                if (existingIndex != last)
                {
                    bindVarId[existingIndex] = bindVarId[last];
                    bindDirection[existingIndex] = bindDirection[last];
                    bindTargetSourceNode[existingIndex] = bindTargetSourceNode[last];
                    bindTargetComponentKind[existingIndex] = bindTargetComponentKind[last];
                    bindPropertyIndexHint[existingIndex] = bindPropertyIndexHint[last];
                    bindPropertyId[existingIndex] = bindPropertyId[last];
                    bindPropertyKind[existingIndex] = bindPropertyKind[last];
                }

                bindVarId[last] = 0;
                bindDirection[last] = 0;
                bindTargetSourceNode[last] = 0;
                bindTargetComponentKind[last] = 0;
                bindPropertyIndexHint[last] = 0;
                bindPropertyId[last] = 0;
                bindPropertyKind[last] = 0;

                bindings.BindingCount = (ushort)(count - 1);
                changed = true;
            }
        }
        else if (existingIndex >= 0)
        {
            if (bindVarId[existingIndex] != variableId)
            {
                bindVarId[existingIndex] = variableId;
                changed = true;
            }
        }
        else
        {
            if (count >= PrefabBindingsComponent.MaxBindings)
            {
                _workspace.ShowToast("Max bindings reached");
                return false;
            }

            int i = count;
            bindVarId[i] = variableId;
            bindDirection[i] = DirectionListBind;
            bindTargetSourceNode[i] = targetSourceNodeStableId;
            bindTargetComponentKind[i] = 0;
            bindPropertyIndexHint[i] = 0;
            bindPropertyId[i] = 0;
            bindPropertyKind[i] = 0;
            bindings.BindingCount = (ushort)(count + 1);
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        bindings.Revision = bindings.Revision + 1;

        var afterBytes = new byte[size];
        UiComponentClipboardPacker.TryPack(_workspace.PropertyWorld, bindingsAny, afterBytes);

        int payloadIndex = _undoComponentSnapshotPayload.Count;
        _undoComponentSnapshotPayload.Add(new ComponentSnapshotUndoPayload
        {
            EntityStableId = prefabStableId,
            ComponentKind = PrefabBindingsComponent.Api.PoolIdConst,
            Before = beforeBytes,
            After = afterBytes
        });
        _undoStack.Push(UndoRecord.ForComponentSnapshot(payloadIndex));
        return true;
    }

    private static AnimationDocument CreateDefaultAnimations()
    {
        var doc = new AnimationDocument();
        doc.CreateTimeline(name: "Timeline 1", durationFrames: 300, snapFps: 60, parentFolderId: doc.RootFolderId);
        return doc;
    }

    public int CreateShape(ShapeKind kind, ImRect rectWorld, int parentFrameId)
    {
        FlushPendingEdits();

        int shapeListIndex = _workspace._shapes.Count;

        UiWorkspace.Shape shape = _workspace.CreateShape(kind, rectWorld, parentFrameId);
        _workspace._shapes.Add(shape);
        UiWorkspace.SetIndexById(_workspace._shapeIndexById, shape.Id, shapeListIndex);

        if (!UiWorkspace.TryGetEntityById(_workspace._prefabEntityById, parentFrameId, out EntityId prefabEntity))
        {
            prefabEntity = EntityId.Null;
        }

        int insertIndex = prefabEntity.IsNull ? 0 : _workspace.World.GetChildCount(prefabEntity);
        EntityId shapeEntity = _workspace.World.CreateEntity(UiNodeType.Shape, prefabEntity);
        UiWorkspace.SetEntityById(_workspace._shapeEntityById, shape.Id, shapeEntity);

        EnsureClipRectArchetypeComponent(shapeEntity);
        EnsureDraggableArchetypeComponent(shapeEntity);
        EnsureConstraintListArchetypeComponent(shapeEntity);

        _workspace.SetComponentWithStableId(shapeEntity, TransformComponentProperties.ToAnyHandle(shape.Transform));
        _workspace.SetComponentWithStableId(shapeEntity, BlendComponentProperties.ToAnyHandle(shape.Blend));
        _workspace.SetComponentWithStableId(shapeEntity, PaintComponentProperties.ToAnyHandle(shape.Paint));

        if (!shape.RectGeometry.IsNull)
        {
            _workspace.SetComponentWithStableId(shapeEntity, RectGeometryComponentProperties.ToAnyHandle(shape.RectGeometry));
        }
        if (!shape.CircleGeometry.IsNull)
        {
            _workspace.SetComponentWithStableId(shapeEntity, CircleGeometryComponentProperties.ToAnyHandle(shape.CircleGeometry));
        }
        if (!shape.Path.IsNull)
        {
            _workspace.SetComponentWithStableId(shapeEntity, PathComponentProperties.ToAnyHandle(shape.Path));
        }

        _workspace.EnsureDefaultLayerName(shapeEntity);

        uint shapeStableId = _workspace.World.GetStableId(shapeEntity);
        uint parentStableId = prefabEntity.IsNull ? 0u : _workspace.World.GetStableId(prefabEntity);
        _undoStack.Push(UndoRecord.ForCreateShape(shape, shapeListIndex, shapeStableId, parentStableId, insertIndex));
        _workspace.BumpPrefabRevision(prefabEntity);
        return shape.Id;
    }

    public int CreateText(ImRect rectWorld, int parentFrameId)
    {
        FlushPendingEdits();

        int textListIndex = _workspace._texts.Count;

        UiWorkspace.Text text = _workspace.CreateText(rectWorld, parentFrameId);
        _workspace._texts.Add(text);
        UiWorkspace.SetIndexById(_workspace._textIndexById, text.Id, textListIndex);

        if (!UiWorkspace.TryGetEntityById(_workspace._prefabEntityById, parentFrameId, out EntityId prefabEntity))
        {
            prefabEntity = EntityId.Null;
        }

        int insertIndex = prefabEntity.IsNull ? 0 : _workspace.World.GetChildCount(prefabEntity);
        EntityId textEntity = _workspace.World.CreateEntity(UiNodeType.Text, prefabEntity);
        UiWorkspace.SetEntityById(_workspace._textEntityById, text.Id, textEntity);
        _workspace.EnsureDefaultLayerName(textEntity);

        EnsureConstraintListArchetypeComponent(textEntity);

        _workspace.SetComponentWithStableId(textEntity, TransformComponentProperties.ToAnyHandle(text.Transform));
        _workspace.SetComponentWithStableId(textEntity, BlendComponentProperties.ToAnyHandle(text.Blend));
        _workspace.SetComponentWithStableId(textEntity, PaintComponentProperties.ToAnyHandle(text.Paint));
        _workspace.SetComponentWithStableId(textEntity, RectGeometryComponentProperties.ToAnyHandle(text.RectGeometry));
        _workspace.SetComponentWithStableId(textEntity, TextComponentProperties.ToAnyHandle(text.Component));

        uint textStableId = _workspace.World.GetStableId(textEntity);
        uint parentStableId = prefabEntity.IsNull ? 0u : _workspace.World.GetStableId(prefabEntity);
        _undoStack.Push(UndoRecord.ForCreateText(text, textListIndex, textStableId, parentStableId, insertIndex));
        _workspace.BumpPrefabRevision(prefabEntity);
        return text.Id;
    }

    public uint CreatePrefabInstance(Vector2 positionWorld, uint sourcePrefabStableId)
    {
        FlushPendingEdits();

        if (sourcePrefabStableId == 0)
        {
            return 0;
        }

        EntityId parentPrefabEntity = _workspace._selectedPrefabEntity;
        if (parentPrefabEntity.IsNull)
        {
            return 0;
        }

        uint parentPrefabStableId = _workspace.World.GetStableId(parentPrefabEntity);
        if (parentPrefabStableId == 0)
        {
            return 0;
        }

        if (sourcePrefabStableId == parentPrefabStableId)
        {
            _workspace.ShowToast("Cannot instance a prefab into itself");
            return 0;
        }

        EntityId sourcePrefabEntity = _workspace.World.GetEntityByStableId(sourcePrefabStableId);
        if (sourcePrefabEntity.IsNull || _workspace.World.GetNodeType(sourcePrefabEntity) != UiNodeType.Prefab)
        {
            _workspace.ShowToast("Invalid source prefab");
            return 0;
        }

        if (!_workspace.TryGetParentLocalPointFromWorldEcs(parentPrefabEntity, positionWorld, out Vector2 positionLocal))
        {
            positionLocal = positionWorld;
        }

        int insertIndex = _workspace.World.GetChildCount(parentPrefabEntity);

        var transformView = TransformComponent.Api.Create(_workspace.PropertyWorld, new TransformComponent
        {
            Position = positionLocal,
            Scale = Vector2.One,
            Rotation = 0f,
            Anchor = Vector2.Zero,
            Depth = 0f
        });

        var blendView = BlendComponent.Api.Create(_workspace.PropertyWorld, new BlendComponent
        {
            IsVisible = true,
            Opacity = 1f,
            BlendMode = (int)PaintBlendMode.Normal
        });

        var instanceView = PrefabInstanceComponent.Api.Create(_workspace.PropertyWorld, new PrefabInstanceComponent
        {
            SourcePrefabStableId = sourcePrefabStableId,
            SourcePrefabRevisionAtBuild = 0,
            SourcePrefabBindingsRevisionAtBuild = 0,
            ValueCount = 0
        });

        if (_workspace.World.TryGetComponent(sourcePrefabEntity, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle prefabVarsAny) &&
            prefabVarsAny.IsValid)
        {
            var prefabVarsHandle = new PrefabVariablesComponentHandle(prefabVarsAny.Index, prefabVarsAny.Generation);
            var prefabVars = PrefabVariablesComponent.Api.FromHandle(_workspace.PropertyWorld, prefabVarsHandle);
            if (prefabVars.IsAlive)
            {
                ushort varCount = prefabVars.VariableCount;
                if (varCount > PrefabInstanceComponent.MaxVariables)
                {
                    varCount = PrefabInstanceComponent.MaxVariables;
                }

                if (varCount > 0)
                {
                    ReadOnlySpan<ushort> srcIds = prefabVars.VariableIdReadOnlySpan();
                    ReadOnlySpan<PropertyValue> srcDefaults = prefabVars.DefaultValueReadOnlySpan();
                    Span<ushort> dstIds = instanceView.VariableIdSpan();
                    Span<PropertyValue> dstValues = instanceView.ValueSpan();

                    for (int i = 0; i < varCount; i++)
                    {
                        dstIds[i] = srcIds[i];
                        dstValues[i] = srcDefaults[i];
                    }
                }

                instanceView.ValueCount = varCount;
            }
        }

        EntityId instanceEntity = _workspace.World.CreateEntity(UiNodeType.PrefabInstance, parentPrefabEntity);
        _workspace.EnsureDefaultLayerName(instanceEntity);
        EnsureConstraintListArchetypeComponent(instanceEntity);
        _workspace.SetComponentWithStableId(instanceEntity, TransformComponentProperties.ToAnyHandle(transformView.Handle));
        _workspace.SetComponentWithStableId(instanceEntity, BlendComponentProperties.ToAnyHandle(blendView.Handle));
        _workspace.SetComponentWithStableId(instanceEntity, new AnyComponentHandle(PrefabInstanceComponent.Api.PoolIdConst, instanceView.Handle.Index, instanceView.Handle.Generation));
        if (_workspace.World.TryGetComponent(sourcePrefabEntity, PrefabCanvasComponent.Api.PoolIdConst, out AnyComponentHandle sourceCanvasAny) && sourceCanvasAny.IsValid)
        {
            var sourceCanvasHandle = new PrefabCanvasComponentHandle(sourceCanvasAny.Index, sourceCanvasAny.Generation);
            var sourceCanvas = PrefabCanvasComponent.Api.FromHandle(_workspace.PropertyWorld, sourceCanvasHandle);
            if (sourceCanvas.IsAlive)
            {
                var canvasView = PrefabCanvasComponent.Api.Create(_workspace.PropertyWorld, new PrefabCanvasComponent { Size = sourceCanvas.Size });
                _workspace.SetComponentWithStableId(instanceEntity, PrefabCanvasComponentProperties.ToAnyHandle(canvasView.Handle));
            }
        }

        uint instanceStableId = _workspace.World.GetStableId(instanceEntity);
        _undoStack.Push(UndoRecord.ForCreatePrefabInstance(instanceStableId, parentPrefabStableId, insertIndex));
        _workspace.BumpPrefabRevision(parentPrefabEntity);
        _workspace.SelectSingleEntity(instanceEntity);
        return instanceStableId;
    }

    public int CreatePolygon(ReadOnlySpan<Vector2> pointsWorld, int parentFrameId)
    {
        FlushPendingEdits();

        int pointCount = pointsWorld.Length;
        if (pointCount < 3)
        {
            return 0;
        }

        float minX = pointsWorld[0].X;
        float minY = pointsWorld[0].Y;
        float maxX = minX;
        float maxY = minY;

        for (int i = 1; i < pointCount; i++)
        {
            Vector2 p = pointsWorld[i];
            if (p.X < minX)
            {
                minX = p.X;
            }
            if (p.Y < minY)
            {
                minY = p.Y;
            }
            if (p.X > maxX)
            {
                maxX = p.X;
            }
            if (p.Y > maxY)
            {
                maxY = p.Y;
            }
        }

        var rectWorld = ImRect.FromMinMax(minX, minY, maxX, maxY);

        int pointStart = _workspace._polygonPointsLocal.Count;
        Vector2 offsetWorld = new Vector2(rectWorld.X, rectWorld.Y);
        for (int i = 0; i < pointCount; i++)
        {
            _workspace._polygonPointsLocal.Add(pointsWorld[i] - offsetWorld);
        }

        var propertyWorld = _workspace.PropertyWorld;

        var shapeView = ShapeComponent.Api.Create(propertyWorld, new ShapeComponent { Kind = ShapeKind.Polygon });
        var transformView = TransformComponent.Api.Create(propertyWorld, new TransformComponent
        {
            Position = new Vector2(rectWorld.X, rectWorld.Y),
            Scale = Vector2.One,
            Rotation = 0f,
            Anchor = Vector2.Zero,
            Depth = 0f
        });

        var blendView = BlendComponent.Api.Create(propertyWorld, new BlendComponent
        {
            IsVisible = true,
            Opacity = 1f,
            BlendMode = (int)PaintBlendMode.Normal
        });

        var paintView = PaintComponent.Api.Create(propertyWorld, new PaintComponent
        {
            LayerCount = 1
        });

	        Span<int> layerKind = PaintComponentProperties.LayerKindArray(propertyWorld, paintView.Handle);
	        Span<bool> layerIsVisible = PaintComponentProperties.LayerIsVisibleArray(propertyWorld, paintView.Handle);
	        Span<bool> layerInheritBlend = PaintComponentProperties.LayerInheritBlendModeArray(propertyWorld, paintView.Handle);
	        Span<int> layerBlendMode = PaintComponentProperties.LayerBlendModeArray(propertyWorld, paintView.Handle);
	        Span<float> layerOpacity = PaintComponentProperties.LayerOpacityArray(propertyWorld, paintView.Handle);
	        Span<Vector2> layerOffset = PaintComponentProperties.LayerOffsetArray(propertyWorld, paintView.Handle);
	        Span<float> layerBlur = PaintComponentProperties.LayerBlurArray(propertyWorld, paintView.Handle);
	        Span<int> layerBlurDirection = PaintComponentProperties.LayerBlurDirectionArray(propertyWorld, paintView.Handle);

        layerKind[0] = (int)PaintLayerKind.Fill;
	        layerIsVisible[0] = true;
	        layerInheritBlend[0] = true;
	        layerBlendMode[0] = (int)PaintBlendMode.Normal;
	        layerOpacity[0] = 1f;
	        layerOffset[0] = Vector2.Zero;
	        layerBlur[0] = 0f;
	        layerBlurDirection[0] = 0;

        Span<Color32> fillColor = PaintComponentProperties.FillColorArray(propertyWorld, paintView.Handle);
        Span<bool> fillUseGradient = PaintComponentProperties.FillUseGradientArray(propertyWorld, paintView.Handle);
        Span<Color32> fillGradientColorA = PaintComponentProperties.FillGradientColorAArray(propertyWorld, paintView.Handle);
        Span<Color32> fillGradientColorB = PaintComponentProperties.FillGradientColorBArray(propertyWorld, paintView.Handle);
        Span<int> fillGradientType = PaintComponentProperties.FillGradientTypeArray(propertyWorld, paintView.Handle);
        Span<float> fillGradientMix = PaintComponentProperties.FillGradientMixArray(propertyWorld, paintView.Handle);
        Span<Vector2> fillGradientDirection = PaintComponentProperties.FillGradientDirectionArray(propertyWorld, paintView.Handle);
        Span<Vector2> fillGradientCenter = PaintComponentProperties.FillGradientCenterArray(propertyWorld, paintView.Handle);
        Span<float> fillGradientRadius = PaintComponentProperties.FillGradientRadiusArray(propertyWorld, paintView.Handle);
        Span<float> fillGradientAngle = PaintComponentProperties.FillGradientAngleArray(propertyWorld, paintView.Handle);
        Span<int> fillStopCount = PaintComponentProperties.FillGradientStopCountArray(propertyWorld, paintView.Handle);

        fillColor[0] = new Color32(210, 210, 220, 255);
        fillUseGradient[0] = false;
        fillGradientColorA[0] = new Color32(255, 170, 140, 255);
        fillGradientColorB[0] = new Color32(120, 190, 255, 255);
        fillGradientType[0] = 0;
        fillGradientMix[0] = 0.5f;
        fillGradientDirection[0] = new Vector2(1f, 0f);
        fillGradientCenter[0] = Vector2.Zero;
        fillGradientRadius[0] = 1f;
        fillGradientAngle[0] = 0f;
        fillStopCount[0] = 0;

        int shapeListIndex = _workspace._shapes.Count;
        var shape = new UiWorkspace.Shape
        {
            Id = _workspace._nextShapeId++,
            ParentFrameId = parentFrameId,
            ParentGroupId = 0,
            ParentShapeId = 0,
            Component = shapeView.Handle,
            Transform = transformView.Handle,
            Blend = blendView.Handle,
            Paint = paintView.Handle,
            RectGeometry = default,
            CircleGeometry = default,
            PointStart = pointStart,
            PointCount = pointCount
        };

        _workspace._shapes.Add(shape);
        UiWorkspace.SetIndexById(_workspace._shapeIndexById, shape.Id, shapeListIndex);

        if (!UiWorkspace.TryGetEntityById(_workspace._prefabEntityById, parentFrameId, out EntityId prefabEntity))
        {
            prefabEntity = EntityId.Null;
        }

        int insertIndex = prefabEntity.IsNull ? 0 : _workspace.World.GetChildCount(prefabEntity);
        EntityId shapeEntity = _workspace.World.CreateEntity(UiNodeType.Shape, prefabEntity);
        UiWorkspace.SetEntityById(_workspace._shapeEntityById, shape.Id, shapeEntity);

        EnsureClipRectArchetypeComponent(shapeEntity);
        EnsureDraggableArchetypeComponent(shapeEntity);
        EnsureConstraintListArchetypeComponent(shapeEntity);

        _workspace.SetComponentWithStableId(shapeEntity, TransformComponentProperties.ToAnyHandle(shape.Transform));
        _workspace.SetComponentWithStableId(shapeEntity, BlendComponentProperties.ToAnyHandle(shape.Blend));
        _workspace.SetComponentWithStableId(shapeEntity, PaintComponentProperties.ToAnyHandle(shape.Paint));

        _workspace.EnsureDefaultLayerName(shapeEntity);

        uint shapeStableId = _workspace.World.GetStableId(shapeEntity);
        uint parentStableId = prefabEntity.IsNull ? 0u : _workspace.World.GetStableId(prefabEntity);
        _undoStack.Push(UndoRecord.ForCreateShape(shape, shapeListIndex, shapeStableId, parentStableId, insertIndex));
        return shape.Id;
    }

    public int CreatePath(
        ReadOnlySpan<Vector2> pointsWorld,
        ReadOnlySpan<Vector2> tangentsInLocal,
        ReadOnlySpan<Vector2> tangentsOutLocal,
        ReadOnlySpan<int> vertexKind,
        int parentFrameId)
    {
        FlushPendingEdits();

        int pointCount = pointsWorld.Length;
        if (pointCount < 3 ||
            tangentsInLocal.Length != pointCount ||
            tangentsOutLocal.Length != pointCount ||
            vertexKind.Length != pointCount)
        {
            return 0;
        }

        if (pointCount > PathComponent.MaxVertices)
        {
            return 0;
        }

        float minX = pointsWorld[0].X;
        float minY = pointsWorld[0].Y;
        float maxX = minX;
        float maxY = minY;

        for (int i = 0; i < pointCount; i++)
        {
            Vector2 p = pointsWorld[i];
            if (p.X < minX)
            {
                minX = p.X;
            }
            if (p.Y < minY)
            {
                minY = p.Y;
            }
            if (p.X > maxX)
            {
                maxX = p.X;
            }
            if (p.Y > maxY)
            {
                maxY = p.Y;
            }

            Vector2 handleIn = p + tangentsInLocal[i];
            if (handleIn.X < minX)
            {
                minX = handleIn.X;
            }
            if (handleIn.Y < minY)
            {
                minY = handleIn.Y;
            }
            if (handleIn.X > maxX)
            {
                maxX = handleIn.X;
            }
            if (handleIn.Y > maxY)
            {
                maxY = handleIn.Y;
            }

            Vector2 handleOut = p + tangentsOutLocal[i];
            if (handleOut.X < minX)
            {
                minX = handleOut.X;
            }
            if (handleOut.Y < minY)
            {
                minY = handleOut.Y;
            }
            if (handleOut.X > maxX)
            {
                maxX = handleOut.X;
            }
            if (handleOut.Y > maxY)
            {
                maxY = handleOut.Y;
            }
        }

        var rectWorld = ImRect.FromMinMax(minX, minY, maxX, maxY);
        Vector2 offsetWorld = new Vector2(rectWorld.X, rectWorld.Y);

        var propertyWorld = _workspace.PropertyWorld;

        var shapeView = ShapeComponent.Api.Create(propertyWorld, new ShapeComponent { Kind = ShapeKind.Polygon });
        var transformView = TransformComponent.Api.Create(propertyWorld, new TransformComponent
        {
            Position = new Vector2(rectWorld.X, rectWorld.Y),
            Scale = Vector2.One,
            Rotation = 0f,
            Anchor = Vector2.Zero,
            Depth = 0f
        });

        var blendView = BlendComponent.Api.Create(propertyWorld, new BlendComponent
        {
            IsVisible = true,
            Opacity = 1f,
            BlendMode = (int)PaintBlendMode.Normal
        });

        var pathView = PathComponent.Api.Create(propertyWorld, new PathComponent
        {
            VertexCount = (byte)pointCount,
            PivotLocal = Vector2.Zero
        });

        Span<Vector2> positionLocal = PathComponentProperties.PositionLocalArray(propertyWorld, pathView.Handle);
        Span<Vector2> tangentInLocalSpan = PathComponentProperties.TangentInLocalArray(propertyWorld, pathView.Handle);
        Span<Vector2> tangentOutLocalSpan = PathComponentProperties.TangentOutLocalArray(propertyWorld, pathView.Handle);
        Span<int> vertexKindSpan = PathComponentProperties.VertexKindArray(propertyWorld, pathView.Handle);

        for (int i = 0; i < pointCount; i++)
        {
            positionLocal[i] = pointsWorld[i] - offsetWorld;
            tangentInLocalSpan[i] = tangentsInLocal[i];
            tangentOutLocalSpan[i] = tangentsOutLocal[i];
            vertexKindSpan[i] = vertexKind[i];
        }

        var paintView = PaintComponent.Api.Create(propertyWorld, new PaintComponent
        {
            LayerCount = 1
        });

        Span<int> layerKind = PaintComponentProperties.LayerKindArray(propertyWorld, paintView.Handle);
        Span<bool> layerIsVisible = PaintComponentProperties.LayerIsVisibleArray(propertyWorld, paintView.Handle);
        Span<bool> layerInheritBlend = PaintComponentProperties.LayerInheritBlendModeArray(propertyWorld, paintView.Handle);
        Span<int> layerBlendMode = PaintComponentProperties.LayerBlendModeArray(propertyWorld, paintView.Handle);
        Span<float> layerOpacity = PaintComponentProperties.LayerOpacityArray(propertyWorld, paintView.Handle);
        Span<Vector2> layerOffset = PaintComponentProperties.LayerOffsetArray(propertyWorld, paintView.Handle);
        Span<float> layerBlur = PaintComponentProperties.LayerBlurArray(propertyWorld, paintView.Handle);

        layerKind[0] = (int)PaintLayerKind.Fill;
        layerIsVisible[0] = true;
        layerInheritBlend[0] = true;
        layerBlendMode[0] = (int)PaintBlendMode.Normal;
        layerOpacity[0] = 1f;
        layerOffset[0] = Vector2.Zero;
        layerBlur[0] = 0f;

        Span<Color32> fillColor = PaintComponentProperties.FillColorArray(propertyWorld, paintView.Handle);
        Span<bool> fillUseGradient = PaintComponentProperties.FillUseGradientArray(propertyWorld, paintView.Handle);
        Span<Color32> fillGradientColorA = PaintComponentProperties.FillGradientColorAArray(propertyWorld, paintView.Handle);
        Span<Color32> fillGradientColorB = PaintComponentProperties.FillGradientColorBArray(propertyWorld, paintView.Handle);
        Span<int> fillGradientType = PaintComponentProperties.FillGradientTypeArray(propertyWorld, paintView.Handle);
        Span<float> fillGradientMix = PaintComponentProperties.FillGradientMixArray(propertyWorld, paintView.Handle);
        Span<Vector2> fillGradientDirection = PaintComponentProperties.FillGradientDirectionArray(propertyWorld, paintView.Handle);
        Span<Vector2> fillGradientCenter = PaintComponentProperties.FillGradientCenterArray(propertyWorld, paintView.Handle);
        Span<float> fillGradientRadius = PaintComponentProperties.FillGradientRadiusArray(propertyWorld, paintView.Handle);
        Span<float> fillGradientAngle = PaintComponentProperties.FillGradientAngleArray(propertyWorld, paintView.Handle);
        Span<int> fillStopCount = PaintComponentProperties.FillGradientStopCountArray(propertyWorld, paintView.Handle);

        fillColor[0] = new Color32(210, 210, 220, 255);
        fillUseGradient[0] = false;
        fillGradientColorA[0] = new Color32(255, 170, 140, 255);
        fillGradientColorB[0] = new Color32(120, 190, 255, 255);
        fillGradientType[0] = 0;
        fillGradientMix[0] = 0.5f;
        fillGradientDirection[0] = new Vector2(1f, 0f);
        fillGradientCenter[0] = Vector2.Zero;
        fillGradientRadius[0] = 1f;
        fillGradientAngle[0] = 0f;
        fillStopCount[0] = 0;

        int shapeListIndex = _workspace._shapes.Count;
        var shape = new UiWorkspace.Shape
        {
            Id = _workspace._nextShapeId++,
            ParentFrameId = parentFrameId,
            ParentGroupId = 0,
            ParentShapeId = 0,
            Component = shapeView.Handle,
            Transform = transformView.Handle,
            Blend = blendView.Handle,
            Paint = paintView.Handle,
            RectGeometry = default,
            CircleGeometry = default,
            Path = pathView.Handle,
            PointStart = -1,
            PointCount = 0
        };

        _workspace._shapes.Add(shape);
        UiWorkspace.SetIndexById(_workspace._shapeIndexById, shape.Id, shapeListIndex);

        if (!UiWorkspace.TryGetEntityById(_workspace._prefabEntityById, parentFrameId, out EntityId prefabEntity))
        {
            prefabEntity = EntityId.Null;
        }

        int insertIndex = prefabEntity.IsNull ? 0 : _workspace.World.GetChildCount(prefabEntity);
        EntityId shapeEntity = _workspace.World.CreateEntity(UiNodeType.Shape, prefabEntity);
        UiWorkspace.SetEntityById(_workspace._shapeEntityById, shape.Id, shapeEntity);

        EnsureClipRectArchetypeComponent(shapeEntity);
        EnsureDraggableArchetypeComponent(shapeEntity);
        EnsureConstraintListArchetypeComponent(shapeEntity);

        _workspace.SetComponentWithStableId(shapeEntity, TransformComponentProperties.ToAnyHandle(shape.Transform));
        _workspace.SetComponentWithStableId(shapeEntity, BlendComponentProperties.ToAnyHandle(shape.Blend));
        _workspace.SetComponentWithStableId(shapeEntity, PaintComponentProperties.ToAnyHandle(shape.Paint));
        _workspace.SetComponentWithStableId(shapeEntity, PathComponentProperties.ToAnyHandle(shape.Path));

        _workspace.EnsureDefaultLayerName(shapeEntity);

        uint shapeStableId = _workspace.World.GetStableId(shapeEntity);
        uint parentStableId = prefabEntity.IsNull ? 0u : _workspace.World.GetStableId(prefabEntity);
        _undoStack.Push(UndoRecord.ForCreateShape(shape, shapeListIndex, shapeStableId, parentStableId, insertIndex));
        return shape.Id;
    }

    public int CreateGroupFromSelection()
    {
        FlushPendingEdits();

        int selectionCount = _workspace._selectedEntities.Count;
        if (selectionCount < 1)
        {
            return 0;
        }

        if (selectionCount > 32)
        {
            return 0;
        }

        EntityId parentEntity = _workspace.World.GetParent(_workspace._selectedEntities[0]);
        if (parentEntity.IsNull)
        {
            return 0;
        }

        Span<EntityId> selected = stackalloc EntityId[32];
        Span<int> sourceIndices = stackalloc int[32];
        int movedCount = 0;
        for (int i = 0; i < selectionCount; i++)
        {
            EntityId entity = _workspace._selectedEntities[i];
            if (_workspace.World.GetParent(entity).Value != parentEntity.Value)
            {
                return 0;
            }

            if (!_workspace.World.TryGetChildIndex(parentEntity, entity, out int sourceIndex))
            {
                return 0;
            }

            selected[movedCount] = entity;
            sourceIndices[movedCount] = sourceIndex;
            movedCount++;
        }

        // Sort by parent order so the group receives them in the same visual order.
        for (int i = 1; i < movedCount; i++)
        {
            EntityId entity = selected[i];
            int index = sourceIndices[i];

            int j = i - 1;
            while (j >= 0 && sourceIndices[j] > index)
            {
                selected[j + 1] = selected[j];
                sourceIndices[j + 1] = sourceIndices[j];
                j--;
            }

            selected[j + 1] = entity;
            sourceIndices[j + 1] = index;
        }

        int insertIndex = sourceIndices[0];

        if (!_workspace.TryComputeSelectionBoundsWorldEcs(parentEntity, selected[..movedCount], out ImRect selectionBoundsWorld))
        {
            return 0;
        }

        Vector2 w0 = new Vector2(selectionBoundsWorld.X, selectionBoundsWorld.Y);
        Vector2 w1 = new Vector2(selectionBoundsWorld.Right, selectionBoundsWorld.Y);
        Vector2 w2 = new Vector2(selectionBoundsWorld.Right, selectionBoundsWorld.Bottom);
        Vector2 w3 = new Vector2(selectionBoundsWorld.X, selectionBoundsWorld.Bottom);

        if (!_workspace.TryGetParentLocalPointFromWorldEcs(parentEntity, w0, out Vector2 l0) ||
            !_workspace.TryGetParentLocalPointFromWorldEcs(parentEntity, w1, out Vector2 l1) ||
            !_workspace.TryGetParentLocalPointFromWorldEcs(parentEntity, w2, out Vector2 l2) ||
            !_workspace.TryGetParentLocalPointFromWorldEcs(parentEntity, w3, out Vector2 l3))
        {
            return 0;
        }

        float minX = MathF.Min(MathF.Min(l0.X, l1.X), MathF.Min(l2.X, l3.X));
        float minY = MathF.Min(MathF.Min(l0.Y, l1.Y), MathF.Min(l2.Y, l3.Y));
        float maxX = MathF.Max(MathF.Max(l0.X, l1.X), MathF.Max(l2.X, l3.X));
        float maxY = MathF.Max(MathF.Max(l0.Y, l1.Y), MathF.Max(l2.Y, l3.Y));

        Vector2 sizeLocal = new Vector2(maxX - minX, maxY - minY);
        if (sizeLocal.X <= 0f || sizeLocal.Y <= 0f)
        {
            return 0;
        }

        Vector2 pivotLocal = new Vector2(minX + sizeLocal.X * 0.5f, minY + sizeLocal.Y * 0.5f);

        var transformView = TransformComponent.Api.Create(_workspace.PropertyWorld, new TransformComponent
        {
            Position = pivotLocal,
            Scale = new Vector2(1f, 1f),
            Rotation = 0f,
            Anchor = new Vector2(0.5f, 0.5f),
            Depth = 0f
        });

        var blendView = BlendComponent.Api.Create(_workspace.PropertyWorld, new BlendComponent
        {
            IsVisible = true,
            Opacity = 1f,
            BlendMode = (int)PaintBlendMode.Normal
        });

        var rectGeometryView = RectGeometryComponent.Api.Create(_workspace.PropertyWorld, new RectGeometryComponent
        {
            Size = sizeLocal,
            CornerRadius = Vector4.Zero
        });

        int groupId = _workspace._nextGroupId++;
        int groupIndex = _workspace._groups.Count;

        int parentFrameId = 0;
        int parentGroupId = 0;
        int parentShapeId = 0;

        EntityId owningPrefabEntity = _workspace.FindOwningPrefabEntity(selected[0]);
        if (!owningPrefabEntity.IsNull)
        {
            _workspace.TryGetLegacyPrefabId(owningPrefabEntity, out parentFrameId);
        }

        UiNodeType parentType = _workspace.World.GetNodeType(parentEntity);
        if (parentType == UiNodeType.BooleanGroup)
        {
            _workspace.TryGetLegacyGroupId(parentEntity, out parentGroupId);
        }
        else if (parentType == UiNodeType.Shape)
        {
            _workspace.TryGetLegacyShapeId(parentEntity, out parentShapeId);
        }

        var group = new UiWorkspace.BooleanGroup
        {
            Id = groupId,
            ParentFrameId = parentFrameId,
            ParentGroupId = parentGroupId,
            ParentShapeId = parentShapeId,
            Kind = UiWorkspace.GroupKind.Plain,
            OpIndex = 0,
            Component = default,
            MaskComponent = default,
            Transform = transformView.Handle,
            Blend = blendView.Handle
        };

        _workspace._groups.Add(group);
        UiWorkspace.SetIndexById(_workspace._groupIndexById, groupId, groupIndex);

        EntityId groupEntity = _workspace.World.CreateEntity(UiNodeType.BooleanGroup, parentEntity);
        uint groupStableId = _workspace.World.GetStableId(groupEntity);

        EnsureClipRectArchetypeComponent(groupEntity);
        EnsureDraggableArchetypeComponent(groupEntity);
        EnsureConstraintListArchetypeComponent(groupEntity);

        // Reorder group to insertIndex (CreateEntity appends).
        int groupSourceIndex = _workspace.World.GetChildCount(parentEntity) - 1;
        if (groupSourceIndex != insertIndex)
        {
            _workspace.World.MoveChild(parentEntity, groupSourceIndex, insertIndex);
        }

        UiWorkspace.SetEntityById(_workspace._groupEntityById, groupId, groupEntity);
        _workspace.SetComponentWithStableId(groupEntity, TransformComponentProperties.ToAnyHandle(group.Transform));
        _workspace.SetComponentWithStableId(groupEntity, BlendComponentProperties.ToAnyHandle(group.Blend));
        _workspace.SetComponentWithStableId(groupEntity, RectGeometryComponentProperties.ToAnyHandle(rectGeometryView.Handle));
        _workspace.EnsureDefaultLayerName(groupEntity);

        int payloadStart = _undoEntityMovePayload.Count;
        for (int i = 0; i < movedCount; i++)
        {
            _undoEntityMovePayload.Add(new EntityMoveUndoPayload
            {
                EntityStableId = _workspace.World.GetStableId(selected[i]),
                SourceIndex = sourceIndices[i]
            });
        }
        int payloadCount = movedCount;

        for (int i = 0; i < movedCount; i++)
        {
            EntityId entity = selected[i];
            if (!_workspace.World.TryGetChildIndex(parentEntity, entity, out int currentIndex))
            {
                continue;
            }

            _workspace.TryPreserveWorldTransformOnReparent(entity, parentEntity, groupEntity);
            _workspace.World.Reparent(entity, parentEntity, currentIndex, groupEntity, insertIndex: i);
        }

        _workspace.SelectSingleEntity(groupEntity);

        uint parentStableId = _workspace.World.GetStableId(parentEntity);
        _undoStack.Push(UndoRecord.ForCreateBooleanGroup(group, groupIndex, groupStableId, parentStableId, insertIndex, payloadStart, payloadCount));
        _workspace.BumpPrefabRevision(_workspace.FindOwningPrefabEntity(parentEntity));
        _workspace.ShowToast("Created group");
        return groupId;
    }

    public int CreateEmptyGroup()
    {
        return CreateEmptyGroupInternal(UiWorkspace.GroupKind.Plain, opIndex: 0);
    }

    public int CreateEmptyMaskGroup()
    {
        return CreateEmptyGroupInternal(UiWorkspace.GroupKind.Mask, opIndex: 0);
    }

    public int CreateEmptyBooleanGroup(int opIndex)
    {
        return CreateEmptyGroupInternal(UiWorkspace.GroupKind.Boolean, opIndex);
    }

    private int CreateEmptyGroupInternal(UiWorkspace.GroupKind kind, int opIndex)
    {
        FlushPendingEdits();

        if (!TryGetCreateParentFromSelectionOrPrefab(out EntityId parentEntity, out int insertIndex))
        {
            return 0;
        }

        Vector2 pivotLocal = Vector2.Zero;
        if (parentEntity.IsNull)
        {
            return 0;
        }

        RectGeometryComponentHandle rectGeometry = default;
        BooleanGroupComponentHandle booleanComponent = default;
        MaskGroupComponentHandle maskComponent = default;

        if (kind == UiWorkspace.GroupKind.Plain)
        {
            var rectGeometryView = RectGeometryComponent.Api.Create(_workspace.PropertyWorld, new RectGeometryComponent
            {
                Size = new Vector2(100f, 100f),
                CornerRadius = Vector4.Zero
            });
            rectGeometry = rectGeometryView.Handle;
        }
        else if (kind == UiWorkspace.GroupKind.Mask)
        {
            var maskView = MaskGroupComponent.Api.Create(_workspace.PropertyWorld, new MaskGroupComponent
            {
                SoftEdgePx = 2f,
                Invert = false
            });
            maskComponent = maskView.Handle;
        }
        else
        {
            var booleanView = BooleanGroupComponent.Api.Create(_workspace.PropertyWorld, new BooleanGroupComponent
            {
                Smoothness = 12f
            });
            booleanComponent = booleanView.Handle;
        }

        var transformView = TransformComponent.Api.Create(_workspace.PropertyWorld, new TransformComponent
        {
            Position = pivotLocal,
            Scale = new Vector2(1f, 1f),
            Rotation = 0f,
            Anchor = new Vector2(0.5f, 0.5f),
            Depth = 0f
        });

        var blendView = BlendComponent.Api.Create(_workspace.PropertyWorld, new BlendComponent
        {
            IsVisible = true,
            Opacity = 1f,
            BlendMode = (int)PaintBlendMode.Normal
        });

        int groupId = _workspace._nextGroupId++;
        int groupIndex = _workspace._groups.Count;

        int parentFrameId = 0;
        int parentGroupId = 0;
        int parentShapeId = 0;

        EntityId owningPrefabEntity = _workspace.FindOwningPrefabEntity(parentEntity);
        if (!owningPrefabEntity.IsNull)
        {
            _workspace.TryGetLegacyPrefabId(owningPrefabEntity, out parentFrameId);
        }

        UiNodeType parentType = _workspace.World.GetNodeType(parentEntity);
        if (parentType == UiNodeType.BooleanGroup)
        {
            _workspace.TryGetLegacyGroupId(parentEntity, out parentGroupId);
        }
        else if (parentType == UiNodeType.Shape)
        {
            _workspace.TryGetLegacyShapeId(parentEntity, out parentShapeId);
        }

        var group = new UiWorkspace.BooleanGroup
        {
            Id = groupId,
            ParentFrameId = parentFrameId,
            ParentGroupId = parentGroupId,
            ParentShapeId = parentShapeId,
            Kind = kind,
            OpIndex = kind == UiWorkspace.GroupKind.Boolean ? Math.Clamp(opIndex, 0, UiWorkspace.BooleanOpOptions.Length - 1) : 0,
            Component = booleanComponent,
            MaskComponent = maskComponent,
            Transform = transformView.Handle,
            Blend = blendView.Handle
        };

        _workspace._groups.Add(group);
        UiWorkspace.SetIndexById(_workspace._groupIndexById, groupId, groupIndex);

        EntityId groupEntity = _workspace.World.CreateEntity(UiNodeType.BooleanGroup, parentEntity);
        uint groupStableId = _workspace.World.GetStableId(groupEntity);

        EnsureClipRectArchetypeComponent(groupEntity);
        EnsureDraggableArchetypeComponent(groupEntity);
        EnsureConstraintListArchetypeComponent(groupEntity);

        // Reorder group to insertIndex (CreateEntity appends).
        int groupSourceIndex = _workspace.World.GetChildCount(parentEntity) - 1;
        if (groupSourceIndex != insertIndex)
        {
            _workspace.World.MoveChild(parentEntity, groupSourceIndex, insertIndex);
        }

        UiWorkspace.SetEntityById(_workspace._groupEntityById, groupId, groupEntity);
        _workspace.SetComponentWithStableId(groupEntity, TransformComponentProperties.ToAnyHandle(group.Transform));
        _workspace.SetComponentWithStableId(groupEntity, BlendComponentProperties.ToAnyHandle(group.Blend));

        if (kind == UiWorkspace.GroupKind.Plain)
        {
            if (!rectGeometry.IsNull)
            {
                _workspace.SetComponentWithStableId(groupEntity, RectGeometryComponentProperties.ToAnyHandle(rectGeometry));
            }
        }
        else if (kind == UiWorkspace.GroupKind.Mask)
        {
            if (!group.MaskComponent.IsNull)
            {
                _workspace.SetComponentWithStableId(groupEntity, MaskGroupComponentProperties.ToAnyHandle(group.MaskComponent));
            }
        }
        else
        {
            if (!group.Component.IsNull)
            {
                _workspace.SetComponentWithStableId(groupEntity, BooleanGroupComponentProperties.ToAnyHandle(group.Component));
            }
        }

        _workspace.EnsureDefaultLayerName(groupEntity);

        _workspace.SelectSingleEntity(groupEntity);

        uint parentStableId = _workspace.World.GetStableId(parentEntity);
        int payloadStart = _undoEntityMovePayload.Count;
        int payloadCount = 0;

        if (kind == UiWorkspace.GroupKind.Mask)
        {
            _undoStack.Push(UndoRecord.ForCreateMaskGroup(group, groupIndex, groupStableId, parentStableId, insertIndex, payloadStart, payloadCount));
        }
        else
        {
            _undoStack.Push(UndoRecord.ForCreateBooleanGroup(group, groupIndex, groupStableId, parentStableId, insertIndex, payloadStart, payloadCount));
        }

        _workspace.BumpPrefabRevision(_workspace.FindOwningPrefabEntity(parentEntity));

        if (kind == UiWorkspace.GroupKind.Plain)
        {
            _workspace.ShowToast("Created group");
        }
        else if (kind == UiWorkspace.GroupKind.Mask)
        {
            _workspace.ShowToast("Created mask");
        }
        else
        {
            _workspace.ShowToast("Created boolean");
        }

        return groupId;
    }

    private bool TryGetCreateParentFromSelectionOrPrefab(out EntityId parentEntity, out int insertIndex)
    {
        parentEntity = EntityId.Null;
        insertIndex = 0;

        int selectionCount = _workspace._selectedEntities.Count;
        if (selectionCount > 0)
        {
            EntityId candidateParent = _workspace.World.GetParent(_workspace._selectedEntities[0]);
            if (!candidateParent.IsNull)
            {
                bool allSameParent = true;
                int minChildIndex = int.MaxValue;
                for (int i = 0; i < selectionCount; i++)
                {
                    EntityId entity = _workspace._selectedEntities[i];
                    if (_workspace.World.GetParent(entity).Value != candidateParent.Value)
                    {
                        allSameParent = false;
                        break;
                    }

                    if (_workspace.World.TryGetChildIndex(candidateParent, entity, out int childIndex) && childIndex < minChildIndex)
                    {
                        minChildIndex = childIndex;
                    }
                }

                if (allSameParent && minChildIndex != int.MaxValue)
                {
                    parentEntity = candidateParent;
                    insertIndex = minChildIndex;
                    return true;
                }
            }
        }

        parentEntity = _workspace._selectedPrefabEntity;
        if (parentEntity.IsNull)
        {
            return false;
        }

        insertIndex = _workspace.World.GetChildCount(parentEntity);
        return true;
    }

    public int CreateBooleanGroupFromSelection(int opIndex)
    {
        FlushPendingEdits();

        int selectionCount = _workspace._selectedEntities.Count;
        if (selectionCount < 1)
        {
            return 0;
        }

        if (selectionCount > 32)
        {
            return 0;
        }

        EntityId parentEntity = _workspace.World.GetParent(_workspace._selectedEntities[0]);
        if (parentEntity.IsNull)
        {
            return 0;
        }

        Span<EntityId> selected = stackalloc EntityId[32];
        Span<int> sourceIndices = stackalloc int[32];
        int movedCount = 0;
        for (int i = 0; i < selectionCount; i++)
        {
            EntityId entity = _workspace._selectedEntities[i];
            if (_workspace.World.GetParent(entity).Value != parentEntity.Value)
            {
                return 0;
            }

            if (!_workspace.World.TryGetChildIndex(parentEntity, entity, out int sourceIndex))
            {
                return 0;
            }

            selected[movedCount] = entity;
            sourceIndices[movedCount] = sourceIndex;
            movedCount++;
        }

        // Sort by parent order so the group receives them in the same visual order.
        for (int i = 1; i < movedCount; i++)
        {
            EntityId entity = selected[i];
            int index = sourceIndices[i];

            int j = i - 1;
            while (j >= 0 && sourceIndices[j] > index)
            {
                selected[j + 1] = selected[j];
                sourceIndices[j + 1] = sourceIndices[j];
                j--;
            }

            selected[j + 1] = entity;
            sourceIndices[j + 1] = index;
        }

        int insertIndex = sourceIndices[0];

        if (!_workspace.TryComputeSelectionBoundsWorldEcs(parentEntity, selected[..movedCount], out ImRect selectionBoundsWorld))
        {
            return 0;
        }

        Vector2 pivotWorld = new Vector2(selectionBoundsWorld.X + selectionBoundsWorld.Width * 0.5f, selectionBoundsWorld.Y + selectionBoundsWorld.Height * 0.5f);
        if (!_workspace.TryGetParentLocalPointFromWorldEcs(parentEntity, pivotWorld, out Vector2 pivotLocal))
        {
            return 0;
        }

        var booleanView = BooleanGroupComponent.Api.Create(_workspace.PropertyWorld, new BooleanGroupComponent
        {
            Smoothness = 12f
        });

        var transformView = TransformComponent.Api.Create(_workspace.PropertyWorld, new TransformComponent
        {
            Position = pivotLocal,
            Scale = new Vector2(1f, 1f),
            Rotation = 0f,
            Anchor = new Vector2(0.5f, 0.5f),
            Depth = 0f
        });

        var blendView = BlendComponent.Api.Create(_workspace.PropertyWorld, new BlendComponent
        {
            IsVisible = true,
            Opacity = 1f,
            BlendMode = (int)PaintBlendMode.Normal
        });

        int groupId = _workspace._nextGroupId++;
        int groupIndex = _workspace._groups.Count;

        int parentFrameId = 0;
        int parentGroupId = 0;
        int parentShapeId = 0;

        EntityId owningPrefabEntity = _workspace.FindOwningPrefabEntity(selected[0]);
        if (!owningPrefabEntity.IsNull)
        {
            _workspace.TryGetLegacyPrefabId(owningPrefabEntity, out parentFrameId);
        }

        UiNodeType parentType = _workspace.World.GetNodeType(parentEntity);
        if (parentType == UiNodeType.BooleanGroup)
        {
            _workspace.TryGetLegacyGroupId(parentEntity, out parentGroupId);
        }
        else if (parentType == UiNodeType.Shape)
        {
            _workspace.TryGetLegacyShapeId(parentEntity, out parentShapeId);
        }

        var group = new UiWorkspace.BooleanGroup
        {
            Id = groupId,
            ParentFrameId = parentFrameId,
            ParentGroupId = parentGroupId,
            ParentShapeId = parentShapeId,
            OpIndex = Math.Clamp(opIndex, 0, UiWorkspace.BooleanOpOptions.Length - 1),
            Component = booleanView.Handle,
            Transform = transformView.Handle,
            Blend = blendView.Handle
        };

        _workspace._groups.Add(group);
        UiWorkspace.SetIndexById(_workspace._groupIndexById, groupId, groupIndex);

        EntityId groupEntity = _workspace.World.CreateEntity(UiNodeType.BooleanGroup, parentEntity);
        uint groupStableId = _workspace.World.GetStableId(groupEntity);

        EnsureClipRectArchetypeComponent(groupEntity);
        EnsureDraggableArchetypeComponent(groupEntity);
        EnsureConstraintListArchetypeComponent(groupEntity);

        // Reorder group to insertIndex (CreateEntity appends).
        int groupSourceIndex = _workspace.World.GetChildCount(parentEntity) - 1;
        if (groupSourceIndex != insertIndex)
        {
            _workspace.World.MoveChild(parentEntity, groupSourceIndex, insertIndex);
        }

        UiWorkspace.SetEntityById(_workspace._groupEntityById, groupId, groupEntity);
        _workspace.SetComponentWithStableId(groupEntity, BooleanGroupComponentProperties.ToAnyHandle(group.Component));
        _workspace.SetComponentWithStableId(groupEntity, TransformComponentProperties.ToAnyHandle(group.Transform));
        _workspace.SetComponentWithStableId(groupEntity, BlendComponentProperties.ToAnyHandle(group.Blend));
        _workspace.EnsureDefaultLayerName(groupEntity);

        int payloadStart = _undoEntityMovePayload.Count;
        for (int i = 0; i < movedCount; i++)
        {
            _undoEntityMovePayload.Add(new EntityMoveUndoPayload
            {
                EntityStableId = _workspace.World.GetStableId(selected[i]),
                SourceIndex = sourceIndices[i]
            });
        }
        int payloadCount = movedCount;

        for (int i = 0; i < movedCount; i++)
        {
            EntityId entity = selected[i];
            if (!_workspace.World.TryGetChildIndex(parentEntity, entity, out int currentIndex))
            {
                continue;
            }

            _workspace.TryPreserveWorldTransformOnReparent(entity, parentEntity, groupEntity);
            _workspace.World.Reparent(entity, parentEntity, currentIndex, groupEntity, insertIndex: i);
        }

        _workspace.SelectSingleEntity(groupEntity);

        uint parentStableId = _workspace.World.GetStableId(parentEntity);
        _undoStack.Push(UndoRecord.ForCreateBooleanGroup(group, groupIndex, groupStableId, parentStableId, insertIndex, payloadStart, payloadCount));
        _workspace.BumpPrefabRevision(_workspace.FindOwningPrefabEntity(parentEntity));
        _workspace.ShowToast("Created boolean group");
        return groupId;
    }

    public int CreateMaskGroupFromSelection()
    {
        FlushPendingEdits();

        int selectionCount = _workspace._selectedEntities.Count;
        if (selectionCount < 1)
        {
            return 0;
        }

        if (selectionCount > 32)
        {
            return 0;
        }

        EntityId maskEntity = _workspace._selectedEntities[0];
        UiNodeType maskEntityType = _workspace.World.GetNodeType(maskEntity);
        if (maskEntityType != UiNodeType.Shape && maskEntityType != UiNodeType.Text)
        {
            return 0;
        }

        ReadOnlySpan<EntityId> maskChildren = _workspace.World.GetChildren(maskEntity);
        if (!maskChildren.IsEmpty)
        {
            return 0;
        }

        EntityId parentEntity = _workspace.World.GetParent(maskEntity);
        if (parentEntity.IsNull)
        {
            return 0;
        }

        Span<EntityId> selected = selectionCount <= 32 ? stackalloc EntityId[32] : new EntityId[selectionCount];
        Span<int> sourceIndices = selectionCount <= 32 ? stackalloc int[32] : new int[selectionCount];
        int movedCount = 0;

        for (int i = 0; i < selectionCount; i++)
        {
            EntityId entity = _workspace._selectedEntities[i];
            if (_workspace.World.GetParent(entity).Value != parentEntity.Value)
            {
                return 0;
            }

            if (!_workspace.World.TryGetChildIndex(parentEntity, entity, out int sourceIndex))
            {
                return 0;
            }

            selected[movedCount] = entity;
            sourceIndices[movedCount] = sourceIndex;
            movedCount++;
        }

        // Sort by parent order so the group receives them in the same visual order.
        for (int i = 1; i < movedCount; i++)
        {
            EntityId entity = selected[i];
            int index = sourceIndices[i];

            int j = i - 1;
            while (j >= 0 && sourceIndices[j] > index)
            {
                selected[j + 1] = selected[j];
                sourceIndices[j + 1] = sourceIndices[j];
                j--;
            }

            selected[j + 1] = entity;
            sourceIndices[j + 1] = index;
        }

        int insertIndex = sourceIndices[0];

        if (!_workspace.TryComputeSelectionBoundsWorldEcs(parentEntity, selected[..movedCount], out ImRect selectionBoundsWorld))
        {
            return 0;
        }

        Vector2 pivotWorld = new Vector2(selectionBoundsWorld.X + selectionBoundsWorld.Width * 0.5f, selectionBoundsWorld.Y + selectionBoundsWorld.Height * 0.5f);
        if (!_workspace.TryGetParentLocalPointFromWorldEcs(parentEntity, pivotWorld, out Vector2 pivotLocal))
        {
            return 0;
        }

        var maskView = MaskGroupComponent.Api.Create(_workspace.PropertyWorld, new MaskGroupComponent
        {
            SoftEdgePx = 2f,
            Invert = false
        });

        var transformView = TransformComponent.Api.Create(_workspace.PropertyWorld, new TransformComponent
        {
            Position = pivotLocal,
            Scale = new Vector2(1f, 1f),
            Rotation = 0f,
            Anchor = new Vector2(0.5f, 0.5f),
            Depth = 0f
        });

        var blendView = BlendComponent.Api.Create(_workspace.PropertyWorld, new BlendComponent
        {
            IsVisible = true,
            Opacity = 1f,
            BlendMode = (int)PaintBlendMode.Normal
        });

        int groupId = _workspace._nextGroupId++;
        int groupIndex = _workspace._groups.Count;

        int parentFrameId = 0;
        int parentGroupId = 0;
        int parentShapeId = 0;

        EntityId owningPrefabEntity = _workspace.FindOwningPrefabEntity(selected[0]);
        if (!owningPrefabEntity.IsNull)
        {
            _workspace.TryGetLegacyPrefabId(owningPrefabEntity, out parentFrameId);
        }

        UiNodeType parentType = _workspace.World.GetNodeType(parentEntity);
        if (parentType == UiNodeType.BooleanGroup)
        {
            _workspace.TryGetLegacyGroupId(parentEntity, out parentGroupId);
        }
        else if (parentType == UiNodeType.Shape)
        {
            _workspace.TryGetLegacyShapeId(parentEntity, out parentShapeId);
        }

        var group = new UiWorkspace.BooleanGroup
        {
            Id = groupId,
            ParentFrameId = parentFrameId,
            ParentGroupId = parentGroupId,
            ParentShapeId = parentShapeId,
            Kind = UiWorkspace.GroupKind.Mask,
            OpIndex = 0,
            Component = default,
            MaskComponent = maskView.Handle,
            Transform = transformView.Handle,
            Blend = blendView.Handle
        };

        _workspace._groups.Add(group);
        UiWorkspace.SetIndexById(_workspace._groupIndexById, groupId, groupIndex);

        EntityId groupEntity = _workspace.World.CreateEntity(UiNodeType.BooleanGroup, parentEntity);
        uint groupStableId = _workspace.World.GetStableId(groupEntity);

        EnsureClipRectArchetypeComponent(groupEntity);
        EnsureDraggableArchetypeComponent(groupEntity);
        EnsureConstraintListArchetypeComponent(groupEntity);

        // Reorder group to insertIndex (CreateEntity appends).
        int groupSourceIndex = _workspace.World.GetChildCount(parentEntity) - 1;
        if (groupSourceIndex != insertIndex)
        {
            _workspace.World.MoveChild(parentEntity, groupSourceIndex, insertIndex);
        }

        UiWorkspace.SetEntityById(_workspace._groupEntityById, groupId, groupEntity);
        _workspace.SetComponentWithStableId(groupEntity, MaskGroupComponentProperties.ToAnyHandle(group.MaskComponent));
        _workspace.SetComponentWithStableId(groupEntity, TransformComponentProperties.ToAnyHandle(group.Transform));
        _workspace.SetComponentWithStableId(groupEntity, BlendComponentProperties.ToAnyHandle(group.Blend));
        _workspace.EnsureDefaultLayerName(groupEntity);

        // Build undo payload in reparent order: mask first, then everything else in visual order.
        int maskOriginalIndex = 0;
        for (int i = 0; i < movedCount; i++)
        {
            if (selected[i].Value == maskEntity.Value)
            {
                maskOriginalIndex = sourceIndices[i];
                break;
            }
        }

        int payloadStart = _undoEntityMovePayload.Count;
        _undoEntityMovePayload.Add(new EntityMoveUndoPayload
        {
            EntityStableId = _workspace.World.GetStableId(maskEntity),
            SourceIndex = maskOriginalIndex
        });

        for (int i = 0; i < movedCount; i++)
        {
            EntityId entity = selected[i];
            if (entity.Value == maskEntity.Value)
            {
                continue;
            }

            _undoEntityMovePayload.Add(new EntityMoveUndoPayload
            {
                EntityStableId = _workspace.World.GetStableId(entity),
                SourceIndex = sourceIndices[i]
            });
        }
        int payloadCount = _undoEntityMovePayload.Count - payloadStart;

        // Mask must be child 0.
        if (_workspace.World.TryGetChildIndex(parentEntity, maskEntity, out int currentMaskIndex))
        {
            _workspace.TryPreserveWorldTransformOnReparent(maskEntity, parentEntity, groupEntity);
            _workspace.World.Reparent(maskEntity, parentEntity, currentMaskIndex, groupEntity, insertIndex: 0);
        }

        int insertChildIndex = 1;
        for (int i = 0; i < movedCount; i++)
        {
            EntityId entity = selected[i];
            if (entity.Value == maskEntity.Value)
            {
                continue;
            }

            if (!_workspace.World.TryGetChildIndex(parentEntity, entity, out int currentIndex))
            {
                continue;
            }

            _workspace.TryPreserveWorldTransformOnReparent(entity, parentEntity, groupEntity);
            _workspace.World.Reparent(entity, parentEntity, currentIndex, groupEntity, insertIndex: insertChildIndex);
            insertChildIndex++;
        }

        _workspace.SelectSingleEntity(groupEntity);

        uint parentStableId = _workspace.World.GetStableId(parentEntity);
        _undoStack.Push(UndoRecord.ForCreateMaskGroup(group, groupIndex, groupStableId, parentStableId, insertIndex, payloadStart, payloadCount));
        _workspace.BumpPrefabRevision(_workspace.FindOwningPrefabEntity(parentEntity));
        _workspace.ShowToast("Created mask group");
        return groupId;
    }

    public void RecordTransformComponentEdit(TransformComponentHandle handle, in TransformComponent before, in TransformComponent after)
    {
        FlushPendingEdits();

        if (handle.IsNull || TransformEquals(before, after))
        {
            return;
        }

        _undoStack.Push(UndoRecord.ForTransformComponent(handle, before, after));
        _workspace.BumpSelectedPrefabRevision();
    }

    public void RecordResizeRectShapeEdit(
        TransformComponentHandle transformHandle,
        in TransformComponent transformBefore,
        in TransformComponent transformAfter,
        RectGeometryComponentHandle geometryHandle,
        in RectGeometryComponent geometryBefore,
        in RectGeometryComponent geometryAfter)
    {
        FlushPendingEdits();

        if (transformHandle.IsNull || geometryHandle.IsNull)
        {
            return;
        }

        if (TransformEquals(transformBefore, transformAfter) && RectGeometryEquals(geometryBefore, geometryAfter))
        {
            return;
        }

        _undoStack.Push(UndoRecord.ForResizeRectShape(transformHandle, transformBefore, transformAfter, geometryHandle, geometryBefore, geometryAfter));
        _workspace.BumpSelectedPrefabRevision();
    }

    public void RecordResizeCircleShapeEdit(
        TransformComponentHandle transformHandle,
        in TransformComponent transformBefore,
        in TransformComponent transformAfter,
        CircleGeometryComponentHandle geometryHandle,
        in CircleGeometryComponent geometryBefore,
        in CircleGeometryComponent geometryAfter)
    {
        FlushPendingEdits();

        if (transformHandle.IsNull || geometryHandle.IsNull)
        {
            return;
        }

        if (TransformEquals(transformBefore, transformAfter) && CircleGeometryEquals(geometryBefore, geometryAfter))
        {
            return;
        }

        _undoStack.Push(UndoRecord.ForResizeCircleShape(transformHandle, transformBefore, transformAfter, geometryHandle, geometryBefore, geometryAfter));
        _workspace.BumpSelectedPrefabRevision();
    }

    public void RecordResizePrefabInstanceEdit(
        EntityId instanceEntity,
        TransformComponentHandle transformHandle,
        in TransformComponent transformBefore,
        in TransformComponent transformAfter,
        PrefabCanvasComponentHandle canvasHandle,
        in PrefabCanvasComponent canvasBefore,
        in PrefabCanvasComponent canvasAfter)
    {
        FlushPendingEdits();

        if (instanceEntity.IsNull || transformHandle.IsNull || canvasHandle.IsNull)
        {
            return;
        }

        uint instanceStableId = _workspace.World.GetStableId(instanceEntity);
        if (instanceStableId == 0)
        {
            return;
        }

        byte beforeOverride = 0;
        byte afterOverride = 1;
        if (_workspace.World.TryGetComponent(instanceEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) && instanceAny.IsValid)
        {
            var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var instance = PrefabInstanceComponent.Api.FromHandle(_workspace.PropertyWorld, instanceHandle);
            if (instance.IsAlive)
            {
                beforeOverride = instance.CanvasSizeIsOverridden;
                afterOverride = 1;
                instance.CanvasSizeIsOverridden = afterOverride;
            }
        }

        if (TransformEquals(transformBefore, transformAfter) &&
            canvasBefore.Size == canvasAfter.Size &&
            beforeOverride == afterOverride)
        {
            return;
        }

        _undoStack.Push(UndoRecord.ForResizePrefabInstance(instanceStableId, transformHandle, transformBefore, transformAfter, canvasHandle, canvasBefore, canvasAfter, beforeOverride, afterOverride));
        _workspace.BumpSelectedPrefabRevision();
    }

    public void RecordMovePrefab(int prefabId, Vector2 deltaWorld)
    {
        FlushPendingEdits();

        if (prefabId <= 0 || (deltaWorld.X == 0f && deltaWorld.Y == 0f))
        {
            return;
        }

        _undoStack.Push(UndoRecord.ForMovePrefab(prefabId, deltaWorld));
    }

    public void RecordResizePrefab(int prefabId, ImRect rectWorldBefore, ImRect rectWorldAfter)
    {
        FlushPendingEdits();

        if (prefabId <= 0)
        {
            return;
        }

        if (rectWorldBefore.X == rectWorldAfter.X &&
            rectWorldBefore.Y == rectWorldAfter.Y &&
            rectWorldBefore.Width == rectWorldAfter.Width &&
            rectWorldBefore.Height == rectWorldAfter.Height)
        {
            return;
        }

        _undoStack.Push(UndoRecord.ForResizePrefab(prefabId, rectWorldBefore, rectWorldAfter));
    }

    public void SetBooleanGroupOpIndex(int groupId, int newOpIndex)
    {
        FlushPendingEdits();

        if (groupId <= 0 || !_workspace.TryGetGroupIndexById(groupId, out int groupIndex))
        {
            return;
        }

        var group = _workspace._groups[groupIndex];
        if (group.Kind != UiWorkspace.GroupKind.Boolean)
        {
            return;
        }

        newOpIndex = Math.Clamp(newOpIndex, 0, UiWorkspace.BooleanOpOptions.Length - 1);
        int oldOpIndex = group.OpIndex;
        if (oldOpIndex == newOpIndex)
        {
            return;
        }

        group.OpIndex = newOpIndex;
        _workspace._groups[groupIndex] = group;

        _undoStack.Push(UndoRecord.ForBooleanGroupOpIndex(groupId, oldOpIndex, newOpIndex));
    }

    public void AddSelectionToGroup(int destinationGroupId)
    {
        FlushPendingEdits();

        if (destinationGroupId <= 0)
        {
            return;
        }

        if (!_workspace.TryGetAddToGroupCandidate(out int candidateGroupId) || candidateGroupId != destinationGroupId)
        {
            return;
        }

        if (!UiWorkspace.TryGetEntityById(_workspace._groupEntityById, destinationGroupId, out EntityId groupEntity) || groupEntity.IsNull)
        {
            return;
        }

        EntityId parentEntity = _workspace.World.GetParent(groupEntity);
        if (parentEntity.IsNull)
        {
            return;
        }

        int selectionCount = _workspace._selectedEntities.Count;
        if (selectionCount <= 0 || selectionCount > 32)
        {
            return;
        }

        Span<EntityId> movedEntities = stackalloc EntityId[32];
        Span<int> movedSourceIndices = stackalloc int[32];
        int movedCount = 0;

        for (int i = 0; i < selectionCount; i++)
        {
            EntityId entity = _workspace._selectedEntities[i];
            if (entity.Value == groupEntity.Value)
            {
                continue;
            }

            if (_workspace.World.GetParent(entity).Value != parentEntity.Value)
            {
                return;
            }

            if (!_workspace.World.TryGetChildIndex(parentEntity, entity, out int sourceIndex))
            {
                return;
            }

            movedEntities[movedCount] = entity;
            movedSourceIndices[movedCount] = sourceIndex;
            movedCount++;
        }

        if (movedCount <= 0)
        {
            return;
        }

        // Sort by parent order so the group receives them in the same visual order.
        for (int i = 1; i < movedCount; i++)
        {
            EntityId entity = movedEntities[i];
            int index = movedSourceIndices[i];

            int j = i - 1;
            while (j >= 0 && movedSourceIndices[j] > index)
            {
                movedEntities[j + 1] = movedEntities[j];
                movedSourceIndices[j + 1] = movedSourceIndices[j];
                j--;
            }

            movedEntities[j + 1] = entity;
            movedSourceIndices[j + 1] = index;
        }

        int destStartIndex = _workspace.World.GetChildCount(groupEntity);

        int payloadStart = _undoEntityMovePayload.Count;
        for (int i = 0; i < movedCount; i++)
        {
            _undoEntityMovePayload.Add(new EntityMoveUndoPayload
            {
                EntityStableId = _workspace.World.GetStableId(movedEntities[i]),
                SourceIndex = movedSourceIndices[i]
            });
        }
        int payloadCount = movedCount;

        for (int i = 0; i < movedCount; i++)
        {
            EntityId entity = movedEntities[i];
            if (!_workspace.World.TryGetChildIndex(parentEntity, entity, out int currentIndex))
            {
                continue;
            }

            _workspace.TryPreserveWorldTransformOnReparent(entity, parentEntity, groupEntity);
            _workspace.World.Reparent(entity, parentEntity, currentIndex, groupEntity, destStartIndex + i);
        }

        _workspace.SelectSingleEntity(groupEntity);

        uint groupStableId = _workspace.World.GetStableId(groupEntity);
        uint parentStableId = _workspace.World.GetStableId(parentEntity);
        _undoStack.Push(UndoRecord.ForAddEntitiesToGroup(groupStableId, parentStableId, destStartIndex, movedCount, payloadStart, payloadCount));
        _workspace.BumpPrefabRevision(_workspace.FindOwningPrefabEntity(parentEntity));
    }

    private static bool TransformEquals(in TransformComponent a, in TransformComponent b)
    {
        return a.Position == b.Position &&
            a.Scale == b.Scale &&
            a.Rotation.Equals(b.Rotation) &&
            a.Anchor == b.Anchor &&
            a.Depth.Equals(b.Depth);
    }

    private static bool RectGeometryEquals(in RectGeometryComponent a, in RectGeometryComponent b)
    {
        return a.Size == b.Size && a.CornerRadius == b.CornerRadius;
    }

    private static bool CircleGeometryEquals(in CircleGeometryComponent a, in CircleGeometryComponent b)
    {
        return a.Radius.Equals(b.Radius);
    }

    public bool AddFillPaintLayer(int shapeId, int insertIndex)
    {
        FlushPendingEdits();

        if (!_workspace.TryGetShapeIndexById(shapeId, out int shapeIndex))
        {
            return false;
        }

        var shape = _workspace._shapes[shapeIndex];
        if (shape.Paint.IsNull)
        {
            return false;
        }

        var paintView = PaintComponent.Api.FromHandle(_workspace.PropertyWorld, shape.Paint);
        if (!paintView.IsAlive)
        {
            return false;
        }

        int beforeCount = paintView.LayerCount;
        if (beforeCount >= PaintComponent.MaxLayers)
        {
            return false;
        }

        insertIndex = Math.Clamp(insertIndex, 0, beforeCount);

        PaintComponent before = SnapshotPaintComponent(_workspace.PropertyWorld, shape.Paint);
        InsertPaintLayer(_workspace.PropertyWorld, shape.Paint, insertIndex, PaintLayerKind.Fill);
        PaintComponent after = SnapshotPaintComponent(_workspace.PropertyWorld, shape.Paint);
        _undoStack.Push(UndoRecord.ForPaintComponent(shape.Paint, before, after));
        return true;
    }

    public bool AddFillPaintLayer(EntityId ownerEntity, int insertIndex)
    {
        FlushPendingEdits();

        if (ownerEntity.IsNull)
        {
            return false;
        }

        if (!_workspace.World.TryGetComponent(ownerEntity, PaintComponent.Api.PoolIdConst, out AnyComponentHandle paintAny))
        {
            return false;
        }

        var paintHandle = new PaintComponentHandle(paintAny.Index, paintAny.Generation);
        var paintView = PaintComponent.Api.FromHandle(_workspace.PropertyWorld, paintHandle);
        if (!paintView.IsAlive)
        {
            return false;
        }

        int beforeCount = paintView.LayerCount;
        if (beforeCount >= PaintComponent.MaxLayers)
        {
            return false;
        }

        insertIndex = Math.Clamp(insertIndex, 0, beforeCount);

        PaintComponent before = SnapshotPaintComponent(_workspace.PropertyWorld, paintHandle);
        InsertPaintLayer(_workspace.PropertyWorld, paintHandle, insertIndex, PaintLayerKind.Fill);
        PaintComponent after = SnapshotPaintComponent(_workspace.PropertyWorld, paintHandle);
        _undoStack.Push(UndoRecord.ForPaintComponent(paintHandle, before, after));
        return true;
    }

    public bool AddStrokePaintLayer(int shapeId, int insertIndex)
    {
        FlushPendingEdits();

        if (!_workspace.TryGetShapeIndexById(shapeId, out int shapeIndex))
        {
            return false;
        }

        var shape = _workspace._shapes[shapeIndex];
        if (shape.Paint.IsNull)
        {
            return false;
        }

        var paintView = PaintComponent.Api.FromHandle(_workspace.PropertyWorld, shape.Paint);
        if (!paintView.IsAlive)
        {
            return false;
        }

        int beforeCount = paintView.LayerCount;
        if (beforeCount >= PaintComponent.MaxLayers)
        {
            return false;
        }

        insertIndex = Math.Clamp(insertIndex, 0, beforeCount);

        PaintComponent before = SnapshotPaintComponent(_workspace.PropertyWorld, shape.Paint);
        InsertPaintLayer(_workspace.PropertyWorld, shape.Paint, insertIndex, PaintLayerKind.Stroke);
        PaintComponent after = SnapshotPaintComponent(_workspace.PropertyWorld, shape.Paint);
        _undoStack.Push(UndoRecord.ForPaintComponent(shape.Paint, before, after));
        return true;
    }

    public bool AddStrokePaintLayer(EntityId ownerEntity, int insertIndex)
    {
        FlushPendingEdits();

        if (ownerEntity.IsNull)
        {
            return false;
        }

        if (!_workspace.World.TryGetComponent(ownerEntity, PaintComponent.Api.PoolIdConst, out AnyComponentHandle paintAny))
        {
            return false;
        }

        var paintHandle = new PaintComponentHandle(paintAny.Index, paintAny.Generation);
        var paintView = PaintComponent.Api.FromHandle(_workspace.PropertyWorld, paintHandle);
        if (!paintView.IsAlive)
        {
            return false;
        }

        int beforeCount = paintView.LayerCount;
        if (beforeCount >= PaintComponent.MaxLayers)
        {
            return false;
        }

        insertIndex = Math.Clamp(insertIndex, 0, beforeCount);

        PaintComponent before = SnapshotPaintComponent(_workspace.PropertyWorld, paintHandle);
        InsertPaintLayer(_workspace.PropertyWorld, paintHandle, insertIndex, PaintLayerKind.Stroke);
        PaintComponent after = SnapshotPaintComponent(_workspace.PropertyWorld, paintHandle);
        _undoStack.Push(UndoRecord.ForPaintComponent(paintHandle, before, after));
        return true;
    }

    public bool MovePaintLayer(int shapeId, int sourceIndex, int targetIndex)
    {
        FlushPendingEdits();

        if (!_workspace.TryGetShapeIndexById(shapeId, out int shapeIndex))
        {
            return false;
        }

        var shape = _workspace._shapes[shapeIndex];
        if (shape.Paint.IsNull)
        {
            return false;
        }

        var paintView = PaintComponent.Api.FromHandle(_workspace.PropertyWorld, shape.Paint);
        if (!paintView.IsAlive)
        {
            return false;
        }

        int layerCount = paintView.LayerCount;
        if (layerCount <= 1 || (uint)sourceIndex >= (uint)layerCount)
        {
            return false;
        }

        targetIndex = Math.Clamp(targetIndex, 0, layerCount);
        if (targetIndex == sourceIndex || targetIndex == sourceIndex + 1)
        {
            return false;
        }

        int insertIndex = targetIndex;
        if (sourceIndex < insertIndex)
        {
            insertIndex--;
        }

        PaintComponent before = SnapshotPaintComponent(_workspace.PropertyWorld, shape.Paint);
        MovePaintLayerInPlace(_workspace.PropertyWorld, shape.Paint, sourceIndex, insertIndex);
        PaintComponent after = SnapshotPaintComponent(_workspace.PropertyWorld, shape.Paint);
        _undoStack.Push(UndoRecord.ForPaintComponent(shape.Paint, before, after));
        return true;
    }

    public bool MovePaintLayer(EntityId ownerEntity, int sourceIndex, int targetIndex)
    {
        FlushPendingEdits();

        if (ownerEntity.IsNull)
        {
            return false;
        }

        if (!_workspace.World.TryGetComponent(ownerEntity, PaintComponent.Api.PoolIdConst, out AnyComponentHandle paintAny))
        {
            return false;
        }

        var paintHandle = new PaintComponentHandle(paintAny.Index, paintAny.Generation);
        var paintView = PaintComponent.Api.FromHandle(_workspace.PropertyWorld, paintHandle);
        if (!paintView.IsAlive)
        {
            return false;
        }

        int layerCount = paintView.LayerCount;
        if (layerCount <= 1 || (uint)sourceIndex >= (uint)layerCount)
        {
            return false;
        }

        targetIndex = Math.Clamp(targetIndex, 0, layerCount);
        if (targetIndex == sourceIndex || targetIndex == sourceIndex + 1)
        {
            return false;
        }

        int insertIndex = targetIndex;
        if (sourceIndex < insertIndex)
        {
            insertIndex--;
        }

        PaintComponent before = SnapshotPaintComponent(_workspace.PropertyWorld, paintHandle);
        MovePaintLayerInPlace(_workspace.PropertyWorld, paintHandle, sourceIndex, insertIndex);
        PaintComponent after = SnapshotPaintComponent(_workspace.PropertyWorld, paintHandle);
        _undoStack.Push(UndoRecord.ForPaintComponent(paintHandle, before, after));
        return true;
    }

    public bool RemovePaintLayer(int shapeId, int layerIndex)
    {
        FlushPendingEdits();

        if (!_workspace.TryGetShapeIndexById(shapeId, out int shapeIndex))
        {
            return false;
        }

        var shape = _workspace._shapes[shapeIndex];
        if (shape.Paint.IsNull)
        {
            return false;
        }

        var paintView = PaintComponent.Api.FromHandle(_workspace.PropertyWorld, shape.Paint);
        if (!paintView.IsAlive)
        {
            return false;
        }

        int layerCount = paintView.LayerCount;
        if (layerCount <= 0 || (uint)layerIndex >= (uint)layerCount)
        {
            return false;
        }

        PaintComponent before = SnapshotPaintComponent(_workspace.PropertyWorld, shape.Paint);
        RemovePaintLayerAt(_workspace.PropertyWorld, shape.Paint, layerIndex);
        PaintComponent after = SnapshotPaintComponent(_workspace.PropertyWorld, shape.Paint);
        _undoStack.Push(UndoRecord.ForPaintComponent(shape.Paint, before, after));
        return true;
    }

    public bool RemovePaintLayer(EntityId ownerEntity, int layerIndex)
    {
        FlushPendingEdits();

        if (ownerEntity.IsNull)
        {
            return false;
        }

        if (!_workspace.World.TryGetComponent(ownerEntity, PaintComponent.Api.PoolIdConst, out AnyComponentHandle paintAny))
        {
            return false;
        }

        var paintHandle = new PaintComponentHandle(paintAny.Index, paintAny.Generation);
        var paintView = PaintComponent.Api.FromHandle(_workspace.PropertyWorld, paintHandle);
        if (!paintView.IsAlive)
        {
            return false;
        }

        int layerCount = paintView.LayerCount;
        if (layerCount <= 0 || (uint)layerIndex >= (uint)layerCount)
        {
            return false;
        }

        PaintComponent before = SnapshotPaintComponent(_workspace.PropertyWorld, paintHandle);
        RemovePaintLayerAt(_workspace.PropertyWorld, paintHandle, layerIndex);
        PaintComponent after = SnapshotPaintComponent(_workspace.PropertyWorld, paintHandle);
        _undoStack.Push(UndoRecord.ForPaintComponent(paintHandle, before, after));
        return true;
    }

    private static PaintComponent SnapshotPaintComponent(Pooled.Runtime.IPoolRegistry propertyWorld, PaintComponentHandle paintHandle)
    {
        var view = PaintComponent.Api.FromHandle(propertyWorld, paintHandle);
        if (!view.IsAlive)
        {
            return default;
        }

        var snapshot = new PaintComponent
        {
            LayerCount = view.LayerCount
        };

	        Span<int> layerKind = PaintComponentProperties.LayerKindArray(propertyWorld, paintHandle);
	        Span<bool> layerIsVisible = PaintComponentProperties.LayerIsVisibleArray(propertyWorld, paintHandle);
	        Span<bool> layerInheritBlend = PaintComponentProperties.LayerInheritBlendModeArray(propertyWorld, paintHandle);
	        Span<int> layerBlendMode = PaintComponentProperties.LayerBlendModeArray(propertyWorld, paintHandle);
	        Span<float> layerOpacity = PaintComponentProperties.LayerOpacityArray(propertyWorld, paintHandle);
	        Span<Vector2> layerOffset = PaintComponentProperties.LayerOffsetArray(propertyWorld, paintHandle);
	        Span<float> layerBlur = PaintComponentProperties.LayerBlurArray(propertyWorld, paintHandle);
	        Span<int> layerBlurDirection = PaintComponentProperties.LayerBlurDirectionArray(propertyWorld, paintHandle);

        Span<Color32> fillColor = PaintComponentProperties.FillColorArray(propertyWorld, paintHandle);
        Span<bool> fillUseGradient = PaintComponentProperties.FillUseGradientArray(propertyWorld, paintHandle);
        Span<Color32> fillGradientColorA = PaintComponentProperties.FillGradientColorAArray(propertyWorld, paintHandle);
        Span<Color32> fillGradientColorB = PaintComponentProperties.FillGradientColorBArray(propertyWorld, paintHandle);
        Span<int> fillGradientType = PaintComponentProperties.FillGradientTypeArray(propertyWorld, paintHandle);
        Span<float> fillGradientMix = PaintComponentProperties.FillGradientMixArray(propertyWorld, paintHandle);
        Span<Vector2> fillGradientDirection = PaintComponentProperties.FillGradientDirectionArray(propertyWorld, paintHandle);
        Span<Vector2> fillGradientCenter = PaintComponentProperties.FillGradientCenterArray(propertyWorld, paintHandle);
        Span<float> fillGradientRadius = PaintComponentProperties.FillGradientRadiusArray(propertyWorld, paintHandle);
        Span<float> fillGradientAngle = PaintComponentProperties.FillGradientAngleArray(propertyWorld, paintHandle);
        Span<int> fillStopCount = PaintComponentProperties.FillGradientStopCountArray(propertyWorld, paintHandle);
        Span<Vector4> fillStopT0To3 = PaintComponentProperties.FillGradientStopT0To3Array(propertyWorld, paintHandle);
        Span<Vector4> fillStopT4To7 = PaintComponentProperties.FillGradientStopT4To7Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor0 = PaintComponentProperties.FillGradientStopColor0Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor1 = PaintComponentProperties.FillGradientStopColor1Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor2 = PaintComponentProperties.FillGradientStopColor2Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor3 = PaintComponentProperties.FillGradientStopColor3Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor4 = PaintComponentProperties.FillGradientStopColor4Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor5 = PaintComponentProperties.FillGradientStopColor5Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor6 = PaintComponentProperties.FillGradientStopColor6Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor7 = PaintComponentProperties.FillGradientStopColor7Array(propertyWorld, paintHandle);

        Span<float> strokeWidth = PaintComponentProperties.StrokeWidthArray(propertyWorld, paintHandle);
        Span<Color32> strokeColor = PaintComponentProperties.StrokeColorArray(propertyWorld, paintHandle);
        Span<bool> strokeDashEnabled = PaintComponentProperties.StrokeDashEnabledArray(propertyWorld, paintHandle);
        Span<float> strokeDashLength = PaintComponentProperties.StrokeDashLengthArray(propertyWorld, paintHandle);
        Span<float> strokeDashGapLength = PaintComponentProperties.StrokeDashGapLengthArray(propertyWorld, paintHandle);
        Span<float> strokeDashOffset = PaintComponentProperties.StrokeDashOffsetArray(propertyWorld, paintHandle);
        Span<int> strokeDashCap = PaintComponentProperties.StrokeDashCapArray(propertyWorld, paintHandle);
        Span<float> strokeDashCapSoftness = PaintComponentProperties.StrokeDashCapSoftnessArray(propertyWorld, paintHandle);
        Span<bool> strokeTrimEnabled = PaintComponentProperties.StrokeTrimEnabledArray(propertyWorld, paintHandle);
        Span<float> strokeTrimStart = PaintComponentProperties.StrokeTrimStartArray(propertyWorld, paintHandle);
        Span<float> strokeTrimLength = PaintComponentProperties.StrokeTrimLengthArray(propertyWorld, paintHandle);
        Span<float> strokeTrimOffset = PaintComponentProperties.StrokeTrimOffsetArray(propertyWorld, paintHandle);
        Span<int> strokeTrimCap = PaintComponentProperties.StrokeTrimCapArray(propertyWorld, paintHandle);
        Span<float> strokeTrimCapSoftness = PaintComponentProperties.StrokeTrimCapSoftnessArray(propertyWorld, paintHandle);

	        for (int i = 0; i < PaintComponent.MaxLayers; i++)
	        {
	            snapshot.LayerKind[i] = layerKind[i];
	            snapshot.LayerIsVisible[i] = layerIsVisible[i];
	            snapshot.LayerInheritBlendMode[i] = layerInheritBlend[i];
	            snapshot.LayerBlendMode[i] = layerBlendMode[i];
	            snapshot.LayerOpacity[i] = layerOpacity[i];
	            snapshot.LayerOffset[i] = layerOffset[i];
	            snapshot.LayerBlur[i] = layerBlur[i];
	            snapshot.LayerBlurDirection[i] = layerBlurDirection[i];

            snapshot.FillColor[i] = fillColor[i];
            snapshot.FillUseGradient[i] = fillUseGradient[i];
            snapshot.FillGradientColorA[i] = fillGradientColorA[i];
            snapshot.FillGradientColorB[i] = fillGradientColorB[i];
            snapshot.FillGradientType[i] = fillGradientType[i];
            snapshot.FillGradientMix[i] = fillGradientMix[i];
            snapshot.FillGradientDirection[i] = fillGradientDirection[i];
            snapshot.FillGradientCenter[i] = fillGradientCenter[i];
            snapshot.FillGradientRadius[i] = fillGradientRadius[i];
            snapshot.FillGradientAngle[i] = fillGradientAngle[i];
            snapshot.FillGradientStopCount[i] = fillStopCount[i];
            snapshot.FillGradientStopT0To3[i] = fillStopT0To3[i];
            snapshot.FillGradientStopT4To7[i] = fillStopT4To7[i];
            snapshot.FillGradientStopColor0[i] = fillStopColor0[i];
            snapshot.FillGradientStopColor1[i] = fillStopColor1[i];
            snapshot.FillGradientStopColor2[i] = fillStopColor2[i];
            snapshot.FillGradientStopColor3[i] = fillStopColor3[i];
            snapshot.FillGradientStopColor4[i] = fillStopColor4[i];
            snapshot.FillGradientStopColor5[i] = fillStopColor5[i];
            snapshot.FillGradientStopColor6[i] = fillStopColor6[i];
            snapshot.FillGradientStopColor7[i] = fillStopColor7[i];

            snapshot.StrokeWidth[i] = strokeWidth[i];
            snapshot.StrokeColor[i] = strokeColor[i];
            snapshot.StrokeDashEnabled[i] = strokeDashEnabled[i];
            snapshot.StrokeDashLength[i] = strokeDashLength[i];
            snapshot.StrokeDashGapLength[i] = strokeDashGapLength[i];
            snapshot.StrokeDashOffset[i] = strokeDashOffset[i];
            snapshot.StrokeDashCap[i] = strokeDashCap[i];
            snapshot.StrokeDashCapSoftness[i] = strokeDashCapSoftness[i];
            snapshot.StrokeTrimEnabled[i] = strokeTrimEnabled[i];
            snapshot.StrokeTrimStart[i] = strokeTrimStart[i];
            snapshot.StrokeTrimLength[i] = strokeTrimLength[i];
            snapshot.StrokeTrimOffset[i] = strokeTrimOffset[i];
            snapshot.StrokeTrimCap[i] = strokeTrimCap[i];
            snapshot.StrokeTrimCapSoftness[i] = strokeTrimCapSoftness[i];
        }

        return snapshot;
    }

    private static void ApplyPaintSnapshot(Pooled.Runtime.IPoolRegistry propertyWorld, PaintComponentHandle paintHandle, in PaintComponent value)
    {
        var view = PaintComponent.Api.FromHandle(propertyWorld, paintHandle);
        if (!view.IsAlive)
        {
            return;
        }

        view.LayerCount = value.LayerCount;

        Span<int> layerKind = PaintComponentProperties.LayerKindArray(propertyWorld, paintHandle);
        Span<bool> layerIsVisible = PaintComponentProperties.LayerIsVisibleArray(propertyWorld, paintHandle);
        Span<bool> layerInheritBlend = PaintComponentProperties.LayerInheritBlendModeArray(propertyWorld, paintHandle);
	        Span<int> layerBlendMode = PaintComponentProperties.LayerBlendModeArray(propertyWorld, paintHandle);
	        Span<float> layerOpacity = PaintComponentProperties.LayerOpacityArray(propertyWorld, paintHandle);
	        Span<Vector2> layerOffset = PaintComponentProperties.LayerOffsetArray(propertyWorld, paintHandle);
	        Span<float> layerBlur = PaintComponentProperties.LayerBlurArray(propertyWorld, paintHandle);
	        Span<int> layerBlurDirection = PaintComponentProperties.LayerBlurDirectionArray(propertyWorld, paintHandle);

        Span<Color32> fillColor = PaintComponentProperties.FillColorArray(propertyWorld, paintHandle);
        Span<bool> fillUseGradient = PaintComponentProperties.FillUseGradientArray(propertyWorld, paintHandle);
        Span<Color32> fillGradientColorA = PaintComponentProperties.FillGradientColorAArray(propertyWorld, paintHandle);
        Span<Color32> fillGradientColorB = PaintComponentProperties.FillGradientColorBArray(propertyWorld, paintHandle);
        Span<int> fillGradientType = PaintComponentProperties.FillGradientTypeArray(propertyWorld, paintHandle);
        Span<float> fillGradientMix = PaintComponentProperties.FillGradientMixArray(propertyWorld, paintHandle);
        Span<Vector2> fillGradientDirection = PaintComponentProperties.FillGradientDirectionArray(propertyWorld, paintHandle);
        Span<Vector2> fillGradientCenter = PaintComponentProperties.FillGradientCenterArray(propertyWorld, paintHandle);
        Span<float> fillGradientRadius = PaintComponentProperties.FillGradientRadiusArray(propertyWorld, paintHandle);
        Span<float> fillGradientAngle = PaintComponentProperties.FillGradientAngleArray(propertyWorld, paintHandle);
        Span<int> fillStopCount = PaintComponentProperties.FillGradientStopCountArray(propertyWorld, paintHandle);
        Span<Vector4> fillStopT0To3 = PaintComponentProperties.FillGradientStopT0To3Array(propertyWorld, paintHandle);
        Span<Vector4> fillStopT4To7 = PaintComponentProperties.FillGradientStopT4To7Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor0 = PaintComponentProperties.FillGradientStopColor0Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor1 = PaintComponentProperties.FillGradientStopColor1Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor2 = PaintComponentProperties.FillGradientStopColor2Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor3 = PaintComponentProperties.FillGradientStopColor3Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor4 = PaintComponentProperties.FillGradientStopColor4Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor5 = PaintComponentProperties.FillGradientStopColor5Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor6 = PaintComponentProperties.FillGradientStopColor6Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor7 = PaintComponentProperties.FillGradientStopColor7Array(propertyWorld, paintHandle);

        Span<float> strokeWidth = PaintComponentProperties.StrokeWidthArray(propertyWorld, paintHandle);
        Span<Color32> strokeColor = PaintComponentProperties.StrokeColorArray(propertyWorld, paintHandle);
        Span<bool> strokeDashEnabled = PaintComponentProperties.StrokeDashEnabledArray(propertyWorld, paintHandle);
        Span<float> strokeDashLength = PaintComponentProperties.StrokeDashLengthArray(propertyWorld, paintHandle);
        Span<float> strokeDashGapLength = PaintComponentProperties.StrokeDashGapLengthArray(propertyWorld, paintHandle);
        Span<float> strokeDashOffset = PaintComponentProperties.StrokeDashOffsetArray(propertyWorld, paintHandle);
        Span<int> strokeDashCap = PaintComponentProperties.StrokeDashCapArray(propertyWorld, paintHandle);
        Span<float> strokeDashCapSoftness = PaintComponentProperties.StrokeDashCapSoftnessArray(propertyWorld, paintHandle);
        Span<bool> strokeTrimEnabled = PaintComponentProperties.StrokeTrimEnabledArray(propertyWorld, paintHandle);
        Span<float> strokeTrimStart = PaintComponentProperties.StrokeTrimStartArray(propertyWorld, paintHandle);
        Span<float> strokeTrimLength = PaintComponentProperties.StrokeTrimLengthArray(propertyWorld, paintHandle);
        Span<float> strokeTrimOffset = PaintComponentProperties.StrokeTrimOffsetArray(propertyWorld, paintHandle);
        Span<int> strokeTrimCap = PaintComponentProperties.StrokeTrimCapArray(propertyWorld, paintHandle);
        Span<float> strokeTrimCapSoftness = PaintComponentProperties.StrokeTrimCapSoftnessArray(propertyWorld, paintHandle);

	        for (int i = 0; i < PaintComponent.MaxLayers; i++)
	        {
	            layerKind[i] = value.LayerKind[i];
	            layerIsVisible[i] = value.LayerIsVisible[i];
	            layerInheritBlend[i] = value.LayerInheritBlendMode[i];
	            layerBlendMode[i] = value.LayerBlendMode[i];
	            layerOpacity[i] = value.LayerOpacity[i];
	            layerOffset[i] = value.LayerOffset[i];
	            layerBlur[i] = value.LayerBlur[i];
	            layerBlurDirection[i] = value.LayerBlurDirection[i];

            fillColor[i] = value.FillColor[i];
            fillUseGradient[i] = value.FillUseGradient[i];
            fillGradientColorA[i] = value.FillGradientColorA[i];
            fillGradientColorB[i] = value.FillGradientColorB[i];
            fillGradientType[i] = value.FillGradientType[i];
            fillGradientMix[i] = value.FillGradientMix[i];
            fillGradientDirection[i] = value.FillGradientDirection[i];
            fillGradientCenter[i] = value.FillGradientCenter[i];
            fillGradientRadius[i] = value.FillGradientRadius[i];
            fillGradientAngle[i] = value.FillGradientAngle[i];
            fillStopCount[i] = value.FillGradientStopCount[i];
            fillStopT0To3[i] = value.FillGradientStopT0To3[i];
            fillStopT4To7[i] = value.FillGradientStopT4To7[i];
            fillStopColor0[i] = value.FillGradientStopColor0[i];
            fillStopColor1[i] = value.FillGradientStopColor1[i];
            fillStopColor2[i] = value.FillGradientStopColor2[i];
            fillStopColor3[i] = value.FillGradientStopColor3[i];
            fillStopColor4[i] = value.FillGradientStopColor4[i];
            fillStopColor5[i] = value.FillGradientStopColor5[i];
            fillStopColor6[i] = value.FillGradientStopColor6[i];
            fillStopColor7[i] = value.FillGradientStopColor7[i];

            strokeWidth[i] = value.StrokeWidth[i];
            strokeColor[i] = value.StrokeColor[i];
            strokeDashEnabled[i] = value.StrokeDashEnabled[i];
            strokeDashLength[i] = value.StrokeDashLength[i];
            strokeDashGapLength[i] = value.StrokeDashGapLength[i];
            strokeDashOffset[i] = value.StrokeDashOffset[i];
            strokeDashCap[i] = value.StrokeDashCap[i];
            strokeDashCapSoftness[i] = value.StrokeDashCapSoftness[i];
            strokeTrimEnabled[i] = value.StrokeTrimEnabled[i];
            strokeTrimStart[i] = value.StrokeTrimStart[i];
            strokeTrimLength[i] = value.StrokeTrimLength[i];
            strokeTrimOffset[i] = value.StrokeTrimOffset[i];
            strokeTrimCap[i] = value.StrokeTrimCap[i];
            strokeTrimCapSoftness[i] = value.StrokeTrimCapSoftness[i];
        }
    }

    private static void InsertPaintLayer(Pooled.Runtime.IPoolRegistry propertyWorld, PaintComponentHandle paintHandle, int insertIndex, PaintLayerKind kind)
    {
        var view = PaintComponent.Api.FromHandle(propertyWorld, paintHandle);
        if (!view.IsAlive)
        {
            return;
        }

        int count = view.LayerCount;
        if (count >= PaintComponent.MaxLayers)
        {
            return;
        }

        insertIndex = Math.Clamp(insertIndex, 0, count);

        Span<int> layerKind = PaintComponentProperties.LayerKindArray(propertyWorld, paintHandle);
        Span<bool> layerIsVisible = PaintComponentProperties.LayerIsVisibleArray(propertyWorld, paintHandle);
        Span<bool> layerInheritBlend = PaintComponentProperties.LayerInheritBlendModeArray(propertyWorld, paintHandle);
        Span<int> layerBlendMode = PaintComponentProperties.LayerBlendModeArray(propertyWorld, paintHandle);
	        Span<float> layerOpacity = PaintComponentProperties.LayerOpacityArray(propertyWorld, paintHandle);
	        Span<Vector2> layerOffset = PaintComponentProperties.LayerOffsetArray(propertyWorld, paintHandle);
	        Span<float> layerBlur = PaintComponentProperties.LayerBlurArray(propertyWorld, paintHandle);
	        Span<int> layerBlurDirection = PaintComponentProperties.LayerBlurDirectionArray(propertyWorld, paintHandle);

        Span<Color32> fillColor = PaintComponentProperties.FillColorArray(propertyWorld, paintHandle);
        Span<bool> fillUseGradient = PaintComponentProperties.FillUseGradientArray(propertyWorld, paintHandle);
        Span<Color32> fillGradientColorA = PaintComponentProperties.FillGradientColorAArray(propertyWorld, paintHandle);
        Span<Color32> fillGradientColorB = PaintComponentProperties.FillGradientColorBArray(propertyWorld, paintHandle);
        Span<int> fillGradientType = PaintComponentProperties.FillGradientTypeArray(propertyWorld, paintHandle);
        Span<float> fillGradientMix = PaintComponentProperties.FillGradientMixArray(propertyWorld, paintHandle);
        Span<Vector2> fillGradientDirection = PaintComponentProperties.FillGradientDirectionArray(propertyWorld, paintHandle);
        Span<Vector2> fillGradientCenter = PaintComponentProperties.FillGradientCenterArray(propertyWorld, paintHandle);
        Span<float> fillGradientRadius = PaintComponentProperties.FillGradientRadiusArray(propertyWorld, paintHandle);
        Span<float> fillGradientAngle = PaintComponentProperties.FillGradientAngleArray(propertyWorld, paintHandle);
        Span<int> fillStopCount = PaintComponentProperties.FillGradientStopCountArray(propertyWorld, paintHandle);
        Span<Vector4> fillStopT0To3 = PaintComponentProperties.FillGradientStopT0To3Array(propertyWorld, paintHandle);
        Span<Vector4> fillStopT4To7 = PaintComponentProperties.FillGradientStopT4To7Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor0 = PaintComponentProperties.FillGradientStopColor0Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor1 = PaintComponentProperties.FillGradientStopColor1Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor2 = PaintComponentProperties.FillGradientStopColor2Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor3 = PaintComponentProperties.FillGradientStopColor3Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor4 = PaintComponentProperties.FillGradientStopColor4Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor5 = PaintComponentProperties.FillGradientStopColor5Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor6 = PaintComponentProperties.FillGradientStopColor6Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor7 = PaintComponentProperties.FillGradientStopColor7Array(propertyWorld, paintHandle);

        Span<float> strokeWidth = PaintComponentProperties.StrokeWidthArray(propertyWorld, paintHandle);
        Span<Color32> strokeColor = PaintComponentProperties.StrokeColorArray(propertyWorld, paintHandle);
        Span<bool> strokeDashEnabled = PaintComponentProperties.StrokeDashEnabledArray(propertyWorld, paintHandle);
        Span<float> strokeDashLength = PaintComponentProperties.StrokeDashLengthArray(propertyWorld, paintHandle);
        Span<float> strokeDashGapLength = PaintComponentProperties.StrokeDashGapLengthArray(propertyWorld, paintHandle);
        Span<float> strokeDashOffset = PaintComponentProperties.StrokeDashOffsetArray(propertyWorld, paintHandle);
        Span<int> strokeDashCap = PaintComponentProperties.StrokeDashCapArray(propertyWorld, paintHandle);
        Span<float> strokeDashCapSoftness = PaintComponentProperties.StrokeDashCapSoftnessArray(propertyWorld, paintHandle);
        Span<bool> strokeTrimEnabled = PaintComponentProperties.StrokeTrimEnabledArray(propertyWorld, paintHandle);
        Span<float> strokeTrimStart = PaintComponentProperties.StrokeTrimStartArray(propertyWorld, paintHandle);
        Span<float> strokeTrimLength = PaintComponentProperties.StrokeTrimLengthArray(propertyWorld, paintHandle);
        Span<float> strokeTrimOffset = PaintComponentProperties.StrokeTrimOffsetArray(propertyWorld, paintHandle);
        Span<int> strokeTrimCap = PaintComponentProperties.StrokeTrimCapArray(propertyWorld, paintHandle);
        Span<float> strokeTrimCapSoftness = PaintComponentProperties.StrokeTrimCapSoftnessArray(propertyWorld, paintHandle);

        ShiftUp(layerKind, insertIndex, count);
        ShiftUp(layerIsVisible, insertIndex, count);
        ShiftUp(layerInheritBlend, insertIndex, count);
	        ShiftUp(layerBlendMode, insertIndex, count);
	        ShiftUp(layerOpacity, insertIndex, count);
	        ShiftUp(layerOffset, insertIndex, count);
	        ShiftUp(layerBlur, insertIndex, count);
	        ShiftUp(layerBlurDirection, insertIndex, count);

        ShiftUp(fillColor, insertIndex, count);
        ShiftUp(fillUseGradient, insertIndex, count);
        ShiftUp(fillGradientColorA, insertIndex, count);
        ShiftUp(fillGradientColorB, insertIndex, count);
        ShiftUp(fillGradientType, insertIndex, count);
        ShiftUp(fillGradientMix, insertIndex, count);
        ShiftUp(fillGradientDirection, insertIndex, count);
        ShiftUp(fillGradientCenter, insertIndex, count);
        ShiftUp(fillGradientRadius, insertIndex, count);
        ShiftUp(fillGradientAngle, insertIndex, count);
        ShiftUp(fillStopCount, insertIndex, count);
        ShiftUp(fillStopT0To3, insertIndex, count);
        ShiftUp(fillStopT4To7, insertIndex, count);
        ShiftUp(fillStopColor0, insertIndex, count);
        ShiftUp(fillStopColor1, insertIndex, count);
        ShiftUp(fillStopColor2, insertIndex, count);
        ShiftUp(fillStopColor3, insertIndex, count);
        ShiftUp(fillStopColor4, insertIndex, count);
        ShiftUp(fillStopColor5, insertIndex, count);
        ShiftUp(fillStopColor6, insertIndex, count);
        ShiftUp(fillStopColor7, insertIndex, count);

        ShiftUp(strokeWidth, insertIndex, count);
        ShiftUp(strokeColor, insertIndex, count);
        ShiftUp(strokeDashEnabled, insertIndex, count);
        ShiftUp(strokeDashLength, insertIndex, count);
        ShiftUp(strokeDashGapLength, insertIndex, count);
        ShiftUp(strokeDashOffset, insertIndex, count);
        ShiftUp(strokeDashCap, insertIndex, count);
        ShiftUp(strokeDashCapSoftness, insertIndex, count);
        ShiftUp(strokeTrimEnabled, insertIndex, count);
        ShiftUp(strokeTrimStart, insertIndex, count);
        ShiftUp(strokeTrimLength, insertIndex, count);
        ShiftUp(strokeTrimOffset, insertIndex, count);
        ShiftUp(strokeTrimCap, insertIndex, count);
        ShiftUp(strokeTrimCapSoftness, insertIndex, count);

        view.LayerCount = (byte)(count + 1);
	        InitializePaintLayerDefaults(
	            layerKind,
	            layerIsVisible,
	            layerInheritBlend,
	            layerBlendMode,
	            layerOpacity,
	            layerOffset,
	            layerBlur,
	            layerBlurDirection,
	            fillColor,
	            fillUseGradient,
	            fillGradientColorA,
	            fillGradientColorB,
	            fillGradientType,
            fillGradientMix,
            fillGradientDirection,
            fillGradientCenter,
            fillGradientRadius,
            fillGradientAngle,
            fillStopCount,
            fillStopT0To3,
            fillStopT4To7,
            fillStopColor0,
            fillStopColor1,
            fillStopColor2,
            fillStopColor3,
            fillStopColor4,
            fillStopColor5,
            fillStopColor6,
            fillStopColor7,
            strokeWidth,
            strokeColor,
            strokeDashEnabled,
            strokeDashLength,
            strokeDashGapLength,
            strokeDashOffset,
            strokeDashCap,
            strokeDashCapSoftness,
            strokeTrimEnabled,
            strokeTrimStart,
            strokeTrimLength,
            strokeTrimOffset,
            strokeTrimCap,
            strokeTrimCapSoftness,
            insertIndex,
            kind);
    }

    private static void RemovePaintLayerAt(Pooled.Runtime.IPoolRegistry propertyWorld, PaintComponentHandle paintHandle, int layerIndex)
    {
        var view = PaintComponent.Api.FromHandle(propertyWorld, paintHandle);
        if (!view.IsAlive)
        {
            return;
        }

        int count = view.LayerCount;
        if (count <= 0 || (uint)layerIndex >= (uint)count)
        {
            return;
        }

        Span<int> layerKind = PaintComponentProperties.LayerKindArray(propertyWorld, paintHandle);
        Span<bool> layerIsVisible = PaintComponentProperties.LayerIsVisibleArray(propertyWorld, paintHandle);
        Span<bool> layerInheritBlend = PaintComponentProperties.LayerInheritBlendModeArray(propertyWorld, paintHandle);
        Span<int> layerBlendMode = PaintComponentProperties.LayerBlendModeArray(propertyWorld, paintHandle);
	        Span<float> layerOpacity = PaintComponentProperties.LayerOpacityArray(propertyWorld, paintHandle);
	        Span<Vector2> layerOffset = PaintComponentProperties.LayerOffsetArray(propertyWorld, paintHandle);
	        Span<float> layerBlur = PaintComponentProperties.LayerBlurArray(propertyWorld, paintHandle);
	        Span<int> layerBlurDirection = PaintComponentProperties.LayerBlurDirectionArray(propertyWorld, paintHandle);

        Span<Color32> fillColor = PaintComponentProperties.FillColorArray(propertyWorld, paintHandle);
        Span<bool> fillUseGradient = PaintComponentProperties.FillUseGradientArray(propertyWorld, paintHandle);
        Span<Color32> fillGradientColorA = PaintComponentProperties.FillGradientColorAArray(propertyWorld, paintHandle);
        Span<Color32> fillGradientColorB = PaintComponentProperties.FillGradientColorBArray(propertyWorld, paintHandle);
        Span<int> fillGradientType = PaintComponentProperties.FillGradientTypeArray(propertyWorld, paintHandle);
        Span<float> fillGradientMix = PaintComponentProperties.FillGradientMixArray(propertyWorld, paintHandle);
        Span<Vector2> fillGradientDirection = PaintComponentProperties.FillGradientDirectionArray(propertyWorld, paintHandle);
        Span<Vector2> fillGradientCenter = PaintComponentProperties.FillGradientCenterArray(propertyWorld, paintHandle);
        Span<float> fillGradientRadius = PaintComponentProperties.FillGradientRadiusArray(propertyWorld, paintHandle);
        Span<float> fillGradientAngle = PaintComponentProperties.FillGradientAngleArray(propertyWorld, paintHandle);
        Span<int> fillStopCount = PaintComponentProperties.FillGradientStopCountArray(propertyWorld, paintHandle);
        Span<Vector4> fillStopT0To3 = PaintComponentProperties.FillGradientStopT0To3Array(propertyWorld, paintHandle);
        Span<Vector4> fillStopT4To7 = PaintComponentProperties.FillGradientStopT4To7Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor0 = PaintComponentProperties.FillGradientStopColor0Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor1 = PaintComponentProperties.FillGradientStopColor1Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor2 = PaintComponentProperties.FillGradientStopColor2Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor3 = PaintComponentProperties.FillGradientStopColor3Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor4 = PaintComponentProperties.FillGradientStopColor4Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor5 = PaintComponentProperties.FillGradientStopColor5Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor6 = PaintComponentProperties.FillGradientStopColor6Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor7 = PaintComponentProperties.FillGradientStopColor7Array(propertyWorld, paintHandle);

        Span<float> strokeWidth = PaintComponentProperties.StrokeWidthArray(propertyWorld, paintHandle);
        Span<Color32> strokeColor = PaintComponentProperties.StrokeColorArray(propertyWorld, paintHandle);
        Span<bool> strokeDashEnabled = PaintComponentProperties.StrokeDashEnabledArray(propertyWorld, paintHandle);
        Span<float> strokeDashLength = PaintComponentProperties.StrokeDashLengthArray(propertyWorld, paintHandle);
        Span<float> strokeDashGapLength = PaintComponentProperties.StrokeDashGapLengthArray(propertyWorld, paintHandle);
        Span<float> strokeDashOffset = PaintComponentProperties.StrokeDashOffsetArray(propertyWorld, paintHandle);
        Span<int> strokeDashCap = PaintComponentProperties.StrokeDashCapArray(propertyWorld, paintHandle);
        Span<float> strokeDashCapSoftness = PaintComponentProperties.StrokeDashCapSoftnessArray(propertyWorld, paintHandle);
        Span<bool> strokeTrimEnabled = PaintComponentProperties.StrokeTrimEnabledArray(propertyWorld, paintHandle);
        Span<float> strokeTrimStart = PaintComponentProperties.StrokeTrimStartArray(propertyWorld, paintHandle);
        Span<float> strokeTrimLength = PaintComponentProperties.StrokeTrimLengthArray(propertyWorld, paintHandle);
        Span<float> strokeTrimOffset = PaintComponentProperties.StrokeTrimOffsetArray(propertyWorld, paintHandle);
        Span<int> strokeTrimCap = PaintComponentProperties.StrokeTrimCapArray(propertyWorld, paintHandle);
        Span<float> strokeTrimCapSoftness = PaintComponentProperties.StrokeTrimCapSoftnessArray(propertyWorld, paintHandle);

        ShiftDown(layerKind, layerIndex, count);
        ShiftDown(layerIsVisible, layerIndex, count);
        ShiftDown(layerInheritBlend, layerIndex, count);
	        ShiftDown(layerBlendMode, layerIndex, count);
	        ShiftDown(layerOpacity, layerIndex, count);
	        ShiftDown(layerOffset, layerIndex, count);
	        ShiftDown(layerBlur, layerIndex, count);
	        ShiftDown(layerBlurDirection, layerIndex, count);

        ShiftDown(fillColor, layerIndex, count);
        ShiftDown(fillUseGradient, layerIndex, count);
        ShiftDown(fillGradientColorA, layerIndex, count);
        ShiftDown(fillGradientColorB, layerIndex, count);
        ShiftDown(fillGradientType, layerIndex, count);
        ShiftDown(fillGradientMix, layerIndex, count);
        ShiftDown(fillGradientDirection, layerIndex, count);
        ShiftDown(fillGradientCenter, layerIndex, count);
        ShiftDown(fillGradientRadius, layerIndex, count);
        ShiftDown(fillGradientAngle, layerIndex, count);
        ShiftDown(fillStopCount, layerIndex, count);
        ShiftDown(fillStopT0To3, layerIndex, count);
        ShiftDown(fillStopT4To7, layerIndex, count);
        ShiftDown(fillStopColor0, layerIndex, count);
        ShiftDown(fillStopColor1, layerIndex, count);
        ShiftDown(fillStopColor2, layerIndex, count);
        ShiftDown(fillStopColor3, layerIndex, count);
        ShiftDown(fillStopColor4, layerIndex, count);
        ShiftDown(fillStopColor5, layerIndex, count);
        ShiftDown(fillStopColor6, layerIndex, count);
        ShiftDown(fillStopColor7, layerIndex, count);

        ShiftDown(strokeWidth, layerIndex, count);
        ShiftDown(strokeColor, layerIndex, count);
        ShiftDown(strokeDashEnabled, layerIndex, count);
        ShiftDown(strokeDashLength, layerIndex, count);
        ShiftDown(strokeDashGapLength, layerIndex, count);
        ShiftDown(strokeDashOffset, layerIndex, count);
        ShiftDown(strokeDashCap, layerIndex, count);
        ShiftDown(strokeDashCapSoftness, layerIndex, count);
        ShiftDown(strokeTrimEnabled, layerIndex, count);
        ShiftDown(strokeTrimStart, layerIndex, count);
        ShiftDown(strokeTrimLength, layerIndex, count);
        ShiftDown(strokeTrimOffset, layerIndex, count);
        ShiftDown(strokeTrimCap, layerIndex, count);
        ShiftDown(strokeTrimCapSoftness, layerIndex, count);

        view.LayerCount = (byte)(count - 1);
    }

    private static void MovePaintLayerInPlace(Pooled.Runtime.IPoolRegistry propertyWorld, PaintComponentHandle paintHandle, int sourceIndex, int targetIndex)
    {
        var view = PaintComponent.Api.FromHandle(propertyWorld, paintHandle);
        if (!view.IsAlive)
        {
            return;
        }

        int count = view.LayerCount;
        if (count <= 1 ||
            (uint)sourceIndex >= (uint)count ||
            (uint)targetIndex >= (uint)count ||
            sourceIndex == targetIndex)
        {
            return;
        }

        Span<int> layerKind = PaintComponentProperties.LayerKindArray(propertyWorld, paintHandle);
        Span<bool> layerIsVisible = PaintComponentProperties.LayerIsVisibleArray(propertyWorld, paintHandle);
        Span<bool> layerInheritBlend = PaintComponentProperties.LayerInheritBlendModeArray(propertyWorld, paintHandle);
	        Span<int> layerBlendMode = PaintComponentProperties.LayerBlendModeArray(propertyWorld, paintHandle);
	        Span<float> layerOpacity = PaintComponentProperties.LayerOpacityArray(propertyWorld, paintHandle);
	        Span<Vector2> layerOffset = PaintComponentProperties.LayerOffsetArray(propertyWorld, paintHandle);
	        Span<float> layerBlur = PaintComponentProperties.LayerBlurArray(propertyWorld, paintHandle);
	        Span<int> layerBlurDirection = PaintComponentProperties.LayerBlurDirectionArray(propertyWorld, paintHandle);

	        Span<Color32> fillColor = PaintComponentProperties.FillColorArray(propertyWorld, paintHandle);
        Span<bool> fillUseGradient = PaintComponentProperties.FillUseGradientArray(propertyWorld, paintHandle);
        Span<Color32> fillGradientColorA = PaintComponentProperties.FillGradientColorAArray(propertyWorld, paintHandle);
        Span<Color32> fillGradientColorB = PaintComponentProperties.FillGradientColorBArray(propertyWorld, paintHandle);
        Span<int> fillGradientType = PaintComponentProperties.FillGradientTypeArray(propertyWorld, paintHandle);
        Span<float> fillGradientMix = PaintComponentProperties.FillGradientMixArray(propertyWorld, paintHandle);
        Span<Vector2> fillGradientDirection = PaintComponentProperties.FillGradientDirectionArray(propertyWorld, paintHandle);
        Span<Vector2> fillGradientCenter = PaintComponentProperties.FillGradientCenterArray(propertyWorld, paintHandle);
        Span<float> fillGradientRadius = PaintComponentProperties.FillGradientRadiusArray(propertyWorld, paintHandle);
        Span<float> fillGradientAngle = PaintComponentProperties.FillGradientAngleArray(propertyWorld, paintHandle);
        Span<int> fillStopCount = PaintComponentProperties.FillGradientStopCountArray(propertyWorld, paintHandle);
        Span<Vector4> fillStopT0To3 = PaintComponentProperties.FillGradientStopT0To3Array(propertyWorld, paintHandle);
        Span<Vector4> fillStopT4To7 = PaintComponentProperties.FillGradientStopT4To7Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor0 = PaintComponentProperties.FillGradientStopColor0Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor1 = PaintComponentProperties.FillGradientStopColor1Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor2 = PaintComponentProperties.FillGradientStopColor2Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor3 = PaintComponentProperties.FillGradientStopColor3Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor4 = PaintComponentProperties.FillGradientStopColor4Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor5 = PaintComponentProperties.FillGradientStopColor5Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor6 = PaintComponentProperties.FillGradientStopColor6Array(propertyWorld, paintHandle);
        Span<Color32> fillStopColor7 = PaintComponentProperties.FillGradientStopColor7Array(propertyWorld, paintHandle);

        Span<float> strokeWidth = PaintComponentProperties.StrokeWidthArray(propertyWorld, paintHandle);
        Span<Color32> strokeColor = PaintComponentProperties.StrokeColorArray(propertyWorld, paintHandle);
        Span<bool> strokeDashEnabled = PaintComponentProperties.StrokeDashEnabledArray(propertyWorld, paintHandle);
        Span<float> strokeDashLength = PaintComponentProperties.StrokeDashLengthArray(propertyWorld, paintHandle);
        Span<float> strokeDashGapLength = PaintComponentProperties.StrokeDashGapLengthArray(propertyWorld, paintHandle);
        Span<float> strokeDashOffset = PaintComponentProperties.StrokeDashOffsetArray(propertyWorld, paintHandle);
        Span<int> strokeDashCap = PaintComponentProperties.StrokeDashCapArray(propertyWorld, paintHandle);
        Span<float> strokeDashCapSoftness = PaintComponentProperties.StrokeDashCapSoftnessArray(propertyWorld, paintHandle);
        Span<bool> strokeTrimEnabled = PaintComponentProperties.StrokeTrimEnabledArray(propertyWorld, paintHandle);
        Span<float> strokeTrimStart = PaintComponentProperties.StrokeTrimStartArray(propertyWorld, paintHandle);
        Span<float> strokeTrimLength = PaintComponentProperties.StrokeTrimLengthArray(propertyWorld, paintHandle);
        Span<float> strokeTrimOffset = PaintComponentProperties.StrokeTrimOffsetArray(propertyWorld, paintHandle);
        Span<int> strokeTrimCap = PaintComponentProperties.StrokeTrimCapArray(propertyWorld, paintHandle);
        Span<float> strokeTrimCapSoftness = PaintComponentProperties.StrokeTrimCapSoftnessArray(propertyWorld, paintHandle);

        Move(layerKind, sourceIndex, targetIndex);
        Move(layerIsVisible, sourceIndex, targetIndex);
        Move(layerInheritBlend, sourceIndex, targetIndex);
	        Move(layerBlendMode, sourceIndex, targetIndex);
	        Move(layerOpacity, sourceIndex, targetIndex);
	        Move(layerOffset, sourceIndex, targetIndex);
	        Move(layerBlur, sourceIndex, targetIndex);
	        Move(layerBlurDirection, sourceIndex, targetIndex);

        Move(fillColor, sourceIndex, targetIndex);
        Move(fillUseGradient, sourceIndex, targetIndex);
        Move(fillGradientColorA, sourceIndex, targetIndex);
        Move(fillGradientColorB, sourceIndex, targetIndex);
        Move(fillGradientType, sourceIndex, targetIndex);
        Move(fillGradientMix, sourceIndex, targetIndex);
        Move(fillGradientDirection, sourceIndex, targetIndex);
        Move(fillGradientCenter, sourceIndex, targetIndex);
        Move(fillGradientRadius, sourceIndex, targetIndex);
        Move(fillGradientAngle, sourceIndex, targetIndex);
        Move(fillStopCount, sourceIndex, targetIndex);
        Move(fillStopT0To3, sourceIndex, targetIndex);
        Move(fillStopT4To7, sourceIndex, targetIndex);
        Move(fillStopColor0, sourceIndex, targetIndex);
        Move(fillStopColor1, sourceIndex, targetIndex);
        Move(fillStopColor2, sourceIndex, targetIndex);
        Move(fillStopColor3, sourceIndex, targetIndex);
        Move(fillStopColor4, sourceIndex, targetIndex);
        Move(fillStopColor5, sourceIndex, targetIndex);
        Move(fillStopColor6, sourceIndex, targetIndex);
        Move(fillStopColor7, sourceIndex, targetIndex);

        Move(strokeWidth, sourceIndex, targetIndex);
        Move(strokeColor, sourceIndex, targetIndex);
        Move(strokeDashEnabled, sourceIndex, targetIndex);
        Move(strokeDashLength, sourceIndex, targetIndex);
        Move(strokeDashGapLength, sourceIndex, targetIndex);
        Move(strokeDashOffset, sourceIndex, targetIndex);
        Move(strokeDashCap, sourceIndex, targetIndex);
        Move(strokeDashCapSoftness, sourceIndex, targetIndex);
        Move(strokeTrimEnabled, sourceIndex, targetIndex);
        Move(strokeTrimStart, sourceIndex, targetIndex);
        Move(strokeTrimLength, sourceIndex, targetIndex);
        Move(strokeTrimOffset, sourceIndex, targetIndex);
        Move(strokeTrimCap, sourceIndex, targetIndex);
        Move(strokeTrimCapSoftness, sourceIndex, targetIndex);
    }

    private static void InitializePaintLayerDefaults(
        Span<int> layerKind,
        Span<bool> layerIsVisible,
        Span<bool> layerInheritBlend,
        Span<int> layerBlendMode,
	        Span<float> layerOpacity,
	        Span<Vector2> layerOffset,
	        Span<float> layerBlur,
	        Span<int> layerBlurDirection,
	        Span<Color32> fillColor,
        Span<bool> fillUseGradient,
        Span<Color32> fillGradientColorA,
        Span<Color32> fillGradientColorB,
        Span<int> fillGradientType,
        Span<float> fillGradientMix,
        Span<Vector2> fillGradientDirection,
        Span<Vector2> fillGradientCenter,
        Span<float> fillGradientRadius,
        Span<float> fillGradientAngle,
        Span<int> fillStopCount,
        Span<Vector4> fillStopT0To3,
        Span<Vector4> fillStopT4To7,
        Span<Color32> fillStopColor0,
        Span<Color32> fillStopColor1,
        Span<Color32> fillStopColor2,
        Span<Color32> fillStopColor3,
        Span<Color32> fillStopColor4,
        Span<Color32> fillStopColor5,
        Span<Color32> fillStopColor6,
        Span<Color32> fillStopColor7,
        Span<float> strokeWidth,
        Span<Color32> strokeColor,
        Span<bool> strokeDashEnabled,
        Span<float> strokeDashLength,
        Span<float> strokeDashGapLength,
        Span<float> strokeDashOffset,
        Span<int> strokeDashCap,
        Span<float> strokeDashCapSoftness,
        Span<bool> strokeTrimEnabled,
        Span<float> strokeTrimStart,
        Span<float> strokeTrimLength,
        Span<float> strokeTrimOffset,
        Span<int> strokeTrimCap,
        Span<float> strokeTrimCapSoftness,
        int index,
        PaintLayerKind kind)
    {
        layerKind[index] = (int)kind;
        layerIsVisible[index] = true;
        layerInheritBlend[index] = true;
        layerBlendMode[index] = (int)PaintBlendMode.Normal;
	        layerOpacity[index] = 1f;
	        layerOffset[index] = Vector2.Zero;
	        layerBlur[index] = 0f;
	        layerBlurDirection[index] = 0;

        if (kind == PaintLayerKind.Fill)
        {
            fillColor[index] = new Color32(210, 210, 220, 255);
            fillUseGradient[index] = false;
            fillGradientColorA[index] = new Color32(255, 170, 140, 255);
            fillGradientColorB[index] = new Color32(120, 190, 255, 255);
            fillGradientType[index] = 0;
            fillGradientMix[index] = 0.5f;
            fillGradientDirection[index] = new Vector2(1f, 0f);
            fillGradientCenter[index] = Vector2.Zero;
            fillGradientRadius[index] = 1f;
            fillGradientAngle[index] = 0f;
            fillStopCount[index] = 0;
            fillStopT0To3[index] = Vector4.Zero;
            fillStopT4To7[index] = Vector4.Zero;
            fillStopColor0[index] = default;
            fillStopColor1[index] = default;
            fillStopColor2[index] = default;
            fillStopColor3[index] = default;
            fillStopColor4[index] = default;
            fillStopColor5[index] = default;
            fillStopColor6[index] = default;
            fillStopColor7[index] = default;
            return;
        }

        if (kind == PaintLayerKind.Stroke)
        {
            strokeWidth[index] = 1.5f;
            strokeColor[index] = new Color32(255, 255, 255, 255);
            strokeDashEnabled[index] = false;
            strokeDashLength[index] = 4f;
            strokeDashGapLength[index] = 2f;
            strokeDashOffset[index] = 0f;
            strokeDashCap[index] = 0;
            strokeDashCapSoftness[index] = 12f;

            strokeTrimEnabled[index] = false;
            strokeTrimStart[index] = 0f;
            strokeTrimLength[index] = 100f;
            strokeTrimOffset[index] = 0f;
            strokeTrimCap[index] = 0;
            strokeTrimCapSoftness[index] = 12f;
        }
    }

    private static void ShiftUp<T>(Span<T> span, int insertIndex, int count)
    {
        for (int i = count; i > insertIndex; i--)
        {
            span[i] = span[i - 1];
        }
    }

    private static void ShiftDown<T>(Span<T> span, int removeIndex, int count)
    {
        for (int i = removeIndex; i < count - 1; i++)
        {
            span[i] = span[i + 1];
        }

        span[count - 1] = default!;
    }

    private static void Move<T>(Span<T> span, int sourceIndex, int targetIndex)
    {
        if (sourceIndex == targetIndex)
        {
            return;
        }

        T value = span[sourceIndex];
        if (sourceIndex < targetIndex)
        {
            for (int i = sourceIndex; i < targetIndex; i++)
            {
                span[i] = span[i + 1];
            }
        }
        else
        {
            for (int i = sourceIndex; i > targetIndex; i--)
            {
                span[i] = span[i - 1];
            }
        }

        span[targetIndex] = value;
    }

    public void NotifyPropertyWidgetState(int widgetId, bool isEditing)
    {
        if (!_pendingEdit.IsActive || _pendingEdit.WidgetId != widgetId)
        {
            return;
        }

        if (isEditing)
        {
            return;
        }

        CommitPendingEdit();
    }

    public void ReportPropertyGizmo(EntityId targetEntity, string widgetId, PropertySlot slot, PropertyGizmoPhase phase, int payload0, int payload1)
    {
        if (targetEntity.IsNull || slot.Component.IsNull)
        {
            return;
        }

        _workspace.ReportPropertyGizmo(new PropertyGizmoRequest(slot, targetEntity, Im.Context.GetId(widgetId), phase, payload0, payload1));
    }

    public void ReportPropertyGizmo(EntityId targetEntity, string widgetId, PropertySlot slot, PropertyGizmoPhase phase)
    {
        ReportPropertyGizmo(targetEntity, widgetId, slot, phase, payload0: 0, payload1: 0);
    }

    public void SetPropertyValue(int widgetId, bool isEditing, PropertySlot slot, in PropertyValue newValue)
    {
        if (slot.Component.IsNull)
        {
            return;
        }

        if (!TryResolveSlot(PropertyKey.FromSlot(slot), out PropertySlot resolved))
        {
            return;
        }

        PropertyValue oldValue = ReadValue(_workspace.PropertyWorld, resolved);
        if (oldValue.Equals(resolved.Kind, newValue))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        if (TryApplyAnimationSelectionPropertyEdit(widgetId, isEditing, _activeInspectorEntity, resolved, oldValue, newValue))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        Span<PropertySlot> resolvedSlots = stackalloc PropertySlot[1] { resolved };
        Span<PropertyValue> oldValues = stackalloc PropertyValue[1] { oldValue };
        Span<PropertyValue> newValues = stackalloc PropertyValue[1] { newValue };
        if (TryApplyPrefabExpandedPropertyEdits(widgetId, isEditing, _activeInspectorEntity, resolvedSlots, oldValues, newValues))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }
        if (TryApplyPrefabInstanceRootLayoutEdits(widgetId, isEditing, _activeInspectorEntity, resolvedSlots, oldValues, newValues))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        WriteValue(_workspace.PropertyWorld, resolved, newValue);
        AutoKeyIfTimelineSelected(resolved, oldValue, newValue);

        var key = PropertyKey.FromSlot(resolved);

        if (_pendingEdit.IsActive && _pendingEdit.WidgetId == widgetId &&
            _pendingEdit.PendingRecord.Kind == UndoRecordKind.SetProperty &&
            _pendingEdit.PendingRecord.Property0.PropertyId == key.PropertyId &&
            _pendingEdit.PendingRecord.Property0.Component.Equals(key.Component))
        {
            _pendingEdit.PendingRecord.After0 = newValue;
        }
        else
        {
            if (_pendingEdit.IsActive)
            {
                CommitPendingEdit();
            }

            _pendingEdit = new PendingEdit
            {
                WidgetId = widgetId,
                PendingRecord = UndoRecord.ForProperty(key, oldValue, newValue),
                IsActive = true
            };
        }

        if (!isEditing)
        {
            CommitPendingEdit();
            return;
        }

        NotifyPropertyWidgetState(widgetId, isEditing);
    }

    public void SetPropertyValue(int widgetId, bool isEditing, EntityId editedEntity, PropertySlot slot, in PropertyValue newValue)
    {
        if (slot.Component.IsNull)
        {
            return;
        }

        if (!TryResolveSlot(PropertyKey.FromSlot(slot), out PropertySlot resolved))
        {
            return;
        }

        PropertyValue oldValue = ReadValue(_workspace.PropertyWorld, resolved);
        if (oldValue.Equals(resolved.Kind, newValue))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        if (TryApplyAnimationSelectionPropertyEdit(widgetId, isEditing, editedEntity, resolved, oldValue, newValue))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        Span<PropertySlot> resolvedSlots = stackalloc PropertySlot[1] { resolved };
        Span<PropertyValue> oldValues = stackalloc PropertyValue[1] { oldValue };
        Span<PropertyValue> newValues = stackalloc PropertyValue[1] { newValue };
        if (TryApplyPrefabExpandedPropertyEdits(widgetId, isEditing, editedEntity, resolvedSlots, oldValues, newValues))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }
        if (TryApplyPrefabInstanceRootLayoutEdits(widgetId, isEditing, editedEntity, resolvedSlots, oldValues, newValues))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        WriteValue(_workspace.PropertyWorld, resolved, newValue);
        AutoKeyIfTimelineSelected(resolved, oldValue, newValue);

        var key = PropertyKey.FromSlot(resolved);

        if (_pendingEdit.IsActive && _pendingEdit.WidgetId == widgetId &&
            _pendingEdit.PendingRecord.Kind == UndoRecordKind.SetProperty &&
            _pendingEdit.PendingRecord.Property0.PropertyId == key.PropertyId &&
            _pendingEdit.PendingRecord.Property0.Component.Equals(key.Component))
        {
            _pendingEdit.PendingRecord.After0 = newValue;
        }
        else
        {
            if (_pendingEdit.IsActive)
            {
                CommitPendingEdit();
            }

            _pendingEdit = new PendingEdit
            {
                WidgetId = widgetId,
                PendingRecord = UndoRecord.ForProperty(key, oldValue, newValue),
                IsActive = true
            };
        }

        if (!isEditing)
        {
            CommitPendingEdit();
            return;
        }

        NotifyPropertyWidgetState(widgetId, isEditing);
    }

    public void SetPropertyBatch2(int widgetId, bool isEditing, PropertySlot slot0, in PropertyValue new0, PropertySlot slot1, in PropertyValue new1)
    {
        if (!TryResolveSlot(PropertyKey.FromSlot(slot0), out PropertySlot resolved0) ||
            !TryResolveSlot(PropertyKey.FromSlot(slot1), out PropertySlot resolved1))
        {
            return;
        }

        PropertyValue old0 = ReadValue(_workspace.PropertyWorld, resolved0);
        PropertyValue old1 = ReadValue(_workspace.PropertyWorld, resolved1);

        bool changed0 = !old0.Equals(resolved0.Kind, new0);
        bool changed1 = !old1.Equals(resolved1.Kind, new1);
        if (!changed0 && !changed1)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        if (TryApplyAnimationSelectionPropertyBatch2(widgetId, isEditing, _activeInspectorEntity, resolved0, old0, new0, resolved1, old1, new1))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        Span<PropertySlot> resolvedSlots = stackalloc PropertySlot[2] { resolved0, resolved1 };
        Span<PropertyValue> oldValues = stackalloc PropertyValue[2] { old0, old1 };
        Span<PropertyValue> newValues = stackalloc PropertyValue[2] { new0, new1 };
        if (TryApplyPrefabExpandedPropertyEdits(widgetId, isEditing, _activeInspectorEntity, resolvedSlots, oldValues, newValues))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }
        if (TryApplyPrefabInstanceRootLayoutEdits(widgetId, isEditing, _activeInspectorEntity, resolvedSlots, oldValues, newValues))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        if (changed0)
        {
            WriteValue(_workspace.PropertyWorld, resolved0, new0);
            AutoKeyIfTimelineSelected(resolved0, old0, new0);
        }
        if (changed1)
        {
            WriteValue(_workspace.PropertyWorld, resolved1, new1);
            AutoKeyIfTimelineSelected(resolved1, old1, new1);
        }

        PropertyKey key0 = PropertyKey.FromSlot(resolved0);
        PropertyKey key1 = PropertyKey.FromSlot(resolved1);

        if (_pendingEdit.IsActive && _pendingEdit.WidgetId == widgetId && _pendingEdit.PendingRecord.Kind == UndoRecordKind.SetPropertyBatch &&
            _pendingEdit.PendingRecord.PropertyCount == 2 &&
            _pendingEdit.PendingRecord.Property0.PropertyId == key0.PropertyId &&
            _pendingEdit.PendingRecord.Property1.PropertyId == key1.PropertyId &&
            _pendingEdit.PendingRecord.Property0.Component.Equals(key0.Component) &&
            _pendingEdit.PendingRecord.Property1.Component.Equals(key1.Component))
        {
            _pendingEdit.PendingRecord.After0 = new0;
            _pendingEdit.PendingRecord.After1 = new1;
        }
        else
        {
            if (_pendingEdit.IsActive)
            {
                CommitPendingEdit();
            }

            _pendingEdit = new PendingEdit
            {
                WidgetId = widgetId,
                PendingRecord = UndoRecord.ForPropertyBatch(key0, old0, new0, key1, old1, new1, count: 2),
                IsActive = true
            };
        }

        if (!isEditing)
        {
            CommitPendingEdit();
            return;
        }

        NotifyPropertyWidgetState(widgetId, isEditing);
    }

    public void SetPropertyBatch2(int widgetId, bool isEditing, EntityId editedEntity, PropertySlot slot0, in PropertyValue new0, PropertySlot slot1, in PropertyValue new1)
    {
        if (!TryResolveSlot(PropertyKey.FromSlot(slot0), out PropertySlot resolved0) ||
            !TryResolveSlot(PropertyKey.FromSlot(slot1), out PropertySlot resolved1))
        {
            return;
        }

        PropertyValue old0 = ReadValue(_workspace.PropertyWorld, resolved0);
        PropertyValue old1 = ReadValue(_workspace.PropertyWorld, resolved1);

        bool changed0 = !old0.Equals(resolved0.Kind, new0);
        bool changed1 = !old1.Equals(resolved1.Kind, new1);
        if (!changed0 && !changed1)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        if (TryApplyAnimationSelectionPropertyBatch2(widgetId, isEditing, editedEntity, resolved0, old0, new0, resolved1, old1, new1))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        Span<PropertySlot> resolvedSlots = stackalloc PropertySlot[2] { resolved0, resolved1 };
        Span<PropertyValue> oldValues = stackalloc PropertyValue[2] { old0, old1 };
        Span<PropertyValue> newValues = stackalloc PropertyValue[2] { new0, new1 };
        if (TryApplyPrefabExpandedPropertyEdits(widgetId, isEditing, editedEntity, resolvedSlots, oldValues, newValues))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }
        if (TryApplyPrefabInstanceRootLayoutEdits(widgetId, isEditing, editedEntity, resolvedSlots, oldValues, newValues))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        if (changed0)
        {
            WriteValue(_workspace.PropertyWorld, resolved0, new0);
            AutoKeyIfTimelineSelected(resolved0, old0, new0);
        }
        if (changed1)
        {
            WriteValue(_workspace.PropertyWorld, resolved1, new1);
            AutoKeyIfTimelineSelected(resolved1, old1, new1);
        }

        PropertyKey key0 = PropertyKey.FromSlot(resolved0);
        PropertyKey key1 = PropertyKey.FromSlot(resolved1);

        if (_pendingEdit.IsActive && _pendingEdit.WidgetId == widgetId && _pendingEdit.PendingRecord.Kind == UndoRecordKind.SetPropertyBatch &&
            _pendingEdit.PendingRecord.PropertyCount == 2 &&
            _pendingEdit.PendingRecord.Property0.PropertyId == key0.PropertyId &&
            _pendingEdit.PendingRecord.Property1.PropertyId == key1.PropertyId &&
            _pendingEdit.PendingRecord.Property0.Component.Equals(key0.Component) &&
            _pendingEdit.PendingRecord.Property1.Component.Equals(key1.Component))
        {
            _pendingEdit.PendingRecord.After0 = new0;
            _pendingEdit.PendingRecord.After1 = new1;
        }
        else
        {
            if (_pendingEdit.IsActive)
            {
                CommitPendingEdit();
            }

            _pendingEdit = new PendingEdit
            {
                WidgetId = widgetId,
                PendingRecord = UndoRecord.ForPropertyBatch(key0, old0, new0, key1, old1, new1, count: 2),
                IsActive = true
            };
        }

        if (!isEditing)
        {
            CommitPendingEdit();
            return;
        }

        NotifyPropertyWidgetState(widgetId, isEditing);
    }

    public void SetPropertyBatch3(
        int widgetId,
        bool isEditing,
        PropertySlot slot0,
        in PropertyValue new0,
        PropertySlot slot1,
        in PropertyValue new1,
        PropertySlot slot2,
        in PropertyValue new2)
    {
        if (!TryResolveSlot(PropertyKey.FromSlot(slot0), out PropertySlot resolved0) ||
            !TryResolveSlot(PropertyKey.FromSlot(slot1), out PropertySlot resolved1) ||
            !TryResolveSlot(PropertyKey.FromSlot(slot2), out PropertySlot resolved2))
        {
            return;
        }

        PropertyValue old0 = ReadValue(_workspace.PropertyWorld, resolved0);
        PropertyValue old1 = ReadValue(_workspace.PropertyWorld, resolved1);
        PropertyValue old2 = ReadValue(_workspace.PropertyWorld, resolved2);

        bool changed0 = !old0.Equals(resolved0.Kind, new0);
        bool changed1 = !old1.Equals(resolved1.Kind, new1);
        bool changed2 = !old2.Equals(resolved2.Kind, new2);
        if (!changed0 && !changed1 && !changed2)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        if (TryApplyAnimationSelectionPropertyBatch3(widgetId, isEditing, _activeInspectorEntity, resolved0, old0, new0, resolved1, old1, new1, resolved2, old2, new2))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        Span<PropertySlot> resolvedSlots = stackalloc PropertySlot[3] { resolved0, resolved1, resolved2 };
        Span<PropertyValue> oldValues = stackalloc PropertyValue[3] { old0, old1, old2 };
        Span<PropertyValue> newValues = stackalloc PropertyValue[3] { new0, new1, new2 };
        if (TryApplyPrefabExpandedPropertyEdits(widgetId, isEditing, _activeInspectorEntity, resolvedSlots, oldValues, newValues))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }
        if (TryApplyPrefabInstanceRootLayoutEdits(widgetId, isEditing, _activeInspectorEntity, resolvedSlots, oldValues, newValues))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        if (changed0)
        {
            WriteValue(_workspace.PropertyWorld, resolved0, new0);
            AutoKeyIfTimelineSelected(resolved0, old0, new0);
        }
        if (changed1)
        {
            WriteValue(_workspace.PropertyWorld, resolved1, new1);
            AutoKeyIfTimelineSelected(resolved1, old1, new1);
        }
        if (changed2)
        {
            WriteValue(_workspace.PropertyWorld, resolved2, new2);
            AutoKeyIfTimelineSelected(resolved2, old2, new2);
        }

        PropertyKey key0 = PropertyKey.FromSlot(resolved0);
        PropertyKey key1 = PropertyKey.FromSlot(resolved1);
        PropertyKey key2 = PropertyKey.FromSlot(resolved2);

        if (_pendingEdit.IsActive && _pendingEdit.WidgetId == widgetId && _pendingEdit.PendingRecord.Kind == UndoRecordKind.SetPropertyBatch &&
            _pendingEdit.PendingRecord.PropertyCount == 3 &&
            _pendingEdit.PendingRecord.Property0.PropertyId == key0.PropertyId &&
            _pendingEdit.PendingRecord.Property1.PropertyId == key1.PropertyId &&
            _pendingEdit.PendingRecord.Property2.PropertyId == key2.PropertyId &&
            _pendingEdit.PendingRecord.Property0.Component.Equals(key0.Component) &&
            _pendingEdit.PendingRecord.Property1.Component.Equals(key1.Component) &&
            _pendingEdit.PendingRecord.Property2.Component.Equals(key2.Component))
        {
            _pendingEdit.PendingRecord.After0 = new0;
            _pendingEdit.PendingRecord.After1 = new1;
            _pendingEdit.PendingRecord.After2 = new2;
        }
        else
        {
            if (_pendingEdit.IsActive)
            {
                CommitPendingEdit();
            }

            _pendingEdit = new PendingEdit
            {
                WidgetId = widgetId,
                PendingRecord = UndoRecord.ForPropertyBatch(
                    key0, old0, new0,
                    key1, old1, new1,
                    key2, old2, new2,
                    count: 3),
                IsActive = true
            };
        }

        if (!isEditing)
        {
            CommitPendingEdit();
            return;
        }

        NotifyPropertyWidgetState(widgetId, isEditing);
    }

    public void SetPropertyBatch3(
        int widgetId,
        bool isEditing,
        EntityId editedEntity,
        PropertySlot slot0,
        in PropertyValue new0,
        PropertySlot slot1,
        in PropertyValue new1,
        PropertySlot slot2,
        in PropertyValue new2)
    {
        if (!TryResolveSlot(PropertyKey.FromSlot(slot0), out PropertySlot resolved0) ||
            !TryResolveSlot(PropertyKey.FromSlot(slot1), out PropertySlot resolved1) ||
            !TryResolveSlot(PropertyKey.FromSlot(slot2), out PropertySlot resolved2))
        {
            return;
        }

        PropertyValue old0 = ReadValue(_workspace.PropertyWorld, resolved0);
        PropertyValue old1 = ReadValue(_workspace.PropertyWorld, resolved1);
        PropertyValue old2 = ReadValue(_workspace.PropertyWorld, resolved2);

        bool changed0 = !old0.Equals(resolved0.Kind, new0);
        bool changed1 = !old1.Equals(resolved1.Kind, new1);
        bool changed2 = !old2.Equals(resolved2.Kind, new2);
        if (!changed0 && !changed1 && !changed2)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        if (TryApplyAnimationSelectionPropertyBatch3(widgetId, isEditing, editedEntity, resolved0, old0, new0, resolved1, old1, new1, resolved2, old2, new2))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        Span<PropertySlot> resolvedSlots = stackalloc PropertySlot[3] { resolved0, resolved1, resolved2 };
        Span<PropertyValue> oldValues = stackalloc PropertyValue[3] { old0, old1, old2 };
        Span<PropertyValue> newValues = stackalloc PropertyValue[3] { new0, new1, new2 };
        if (TryApplyPrefabExpandedPropertyEdits(widgetId, isEditing, editedEntity, resolvedSlots, oldValues, newValues))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }
        if (TryApplyPrefabInstanceRootLayoutEdits(widgetId, isEditing, editedEntity, resolvedSlots, oldValues, newValues))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        if (changed0)
        {
            WriteValue(_workspace.PropertyWorld, resolved0, new0);
            AutoKeyIfTimelineSelected(resolved0, old0, new0);
        }
        if (changed1)
        {
            WriteValue(_workspace.PropertyWorld, resolved1, new1);
            AutoKeyIfTimelineSelected(resolved1, old1, new1);
        }
        if (changed2)
        {
            WriteValue(_workspace.PropertyWorld, resolved2, new2);
            AutoKeyIfTimelineSelected(resolved2, old2, new2);
        }

        PropertyKey key0 = PropertyKey.FromSlot(resolved0);
        PropertyKey key1 = PropertyKey.FromSlot(resolved1);
        PropertyKey key2 = PropertyKey.FromSlot(resolved2);

        if (_pendingEdit.IsActive && _pendingEdit.WidgetId == widgetId && _pendingEdit.PendingRecord.Kind == UndoRecordKind.SetPropertyBatch &&
            _pendingEdit.PendingRecord.PropertyCount == 3 &&
            _pendingEdit.PendingRecord.Property0.PropertyId == key0.PropertyId &&
            _pendingEdit.PendingRecord.Property1.PropertyId == key1.PropertyId &&
            _pendingEdit.PendingRecord.Property2.PropertyId == key2.PropertyId &&
            _pendingEdit.PendingRecord.Property0.Component.Equals(key0.Component) &&
            _pendingEdit.PendingRecord.Property1.Component.Equals(key1.Component) &&
            _pendingEdit.PendingRecord.Property2.Component.Equals(key2.Component))
        {
            _pendingEdit.PendingRecord.After0 = new0;
            _pendingEdit.PendingRecord.After1 = new1;
            _pendingEdit.PendingRecord.After2 = new2;
        }
        else
        {
            if (_pendingEdit.IsActive)
            {
                CommitPendingEdit();
            }

            _pendingEdit = new PendingEdit
            {
                WidgetId = widgetId,
                PendingRecord = UndoRecord.ForPropertyBatch(key0, old0, new0, key1, old1, new1, key2, old2, new2, count: 3),
                IsActive = true
            };
        }

        if (!isEditing)
        {
            CommitPendingEdit();
            return;
        }

        NotifyPropertyWidgetState(widgetId, isEditing);
    }

    public void SetPropertyBatch4(
        int widgetId,
        bool isEditing,
        PropertySlot slot0,
        in PropertyValue new0,
        PropertySlot slot1,
        in PropertyValue new1,
        PropertySlot slot2,
        in PropertyValue new2,
        PropertySlot slot3,
        in PropertyValue new3)
    {
        if (!TryResolveSlot(PropertyKey.FromSlot(slot0), out PropertySlot resolved0) ||
            !TryResolveSlot(PropertyKey.FromSlot(slot1), out PropertySlot resolved1) ||
            !TryResolveSlot(PropertyKey.FromSlot(slot2), out PropertySlot resolved2) ||
            !TryResolveSlot(PropertyKey.FromSlot(slot3), out PropertySlot resolved3))
        {
            return;
        }

        PropertyValue old0 = ReadValue(_workspace.PropertyWorld, resolved0);
        PropertyValue old1 = ReadValue(_workspace.PropertyWorld, resolved1);
        PropertyValue old2 = ReadValue(_workspace.PropertyWorld, resolved2);
        PropertyValue old3 = ReadValue(_workspace.PropertyWorld, resolved3);

        bool changed0 = !old0.Equals(resolved0.Kind, new0);
        bool changed1 = !old1.Equals(resolved1.Kind, new1);
        bool changed2 = !old2.Equals(resolved2.Kind, new2);
        bool changed3 = !old3.Equals(resolved3.Kind, new3);
        if (!changed0 && !changed1 && !changed2 && !changed3)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        if (TryApplyAnimationSelectionPropertyBatch4(widgetId, isEditing, _activeInspectorEntity, resolved0, old0, new0, resolved1, old1, new1, resolved2, old2, new2, resolved3, old3, new3))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        Span<PropertySlot> resolvedSlots = stackalloc PropertySlot[4] { resolved0, resolved1, resolved2, resolved3 };
        Span<PropertyValue> oldValues = stackalloc PropertyValue[4] { old0, old1, old2, old3 };
        Span<PropertyValue> newValues = stackalloc PropertyValue[4] { new0, new1, new2, new3 };
        if (TryApplyPrefabExpandedPropertyEdits(widgetId, isEditing, _activeInspectorEntity, resolvedSlots, oldValues, newValues))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }
        if (TryApplyPrefabInstanceRootLayoutEdits(widgetId, isEditing, _activeInspectorEntity, resolvedSlots, oldValues, newValues))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        if (changed0)
        {
            WriteValue(_workspace.PropertyWorld, resolved0, new0);
            AutoKeyIfTimelineSelected(resolved0, old0, new0);
        }
        if (changed1)
        {
            WriteValue(_workspace.PropertyWorld, resolved1, new1);
            AutoKeyIfTimelineSelected(resolved1, old1, new1);
        }
        if (changed2)
        {
            WriteValue(_workspace.PropertyWorld, resolved2, new2);
            AutoKeyIfTimelineSelected(resolved2, old2, new2);
        }
        if (changed3)
        {
            WriteValue(_workspace.PropertyWorld, resolved3, new3);
            AutoKeyIfTimelineSelected(resolved3, old3, new3);
        }

        PropertyKey key0 = PropertyKey.FromSlot(resolved0);
        PropertyKey key1 = PropertyKey.FromSlot(resolved1);
        PropertyKey key2 = PropertyKey.FromSlot(resolved2);
        PropertyKey key3 = PropertyKey.FromSlot(resolved3);

        if (_pendingEdit.IsActive && _pendingEdit.WidgetId == widgetId && _pendingEdit.PendingRecord.Kind == UndoRecordKind.SetPropertyBatch &&
            _pendingEdit.PendingRecord.PropertyCount == 4 &&
            _pendingEdit.PendingRecord.Property0.PropertyId == key0.PropertyId &&
            _pendingEdit.PendingRecord.Property1.PropertyId == key1.PropertyId &&
            _pendingEdit.PendingRecord.Property2.PropertyId == key2.PropertyId &&
            _pendingEdit.PendingRecord.Property3.PropertyId == key3.PropertyId &&
            _pendingEdit.PendingRecord.Property0.Component.Equals(key0.Component) &&
            _pendingEdit.PendingRecord.Property1.Component.Equals(key1.Component) &&
            _pendingEdit.PendingRecord.Property2.Component.Equals(key2.Component) &&
            _pendingEdit.PendingRecord.Property3.Component.Equals(key3.Component))
        {
            _pendingEdit.PendingRecord.After0 = new0;
            _pendingEdit.PendingRecord.After1 = new1;
            _pendingEdit.PendingRecord.After2 = new2;
            _pendingEdit.PendingRecord.After3 = new3;
        }
        else
        {
            if (_pendingEdit.IsActive)
            {
                CommitPendingEdit();
            }

            _pendingEdit = new PendingEdit
            {
                WidgetId = widgetId,
                PendingRecord = UndoRecord.ForPropertyBatch(
                    key0, old0, new0,
                    key1, old1, new1,
                    key2, old2, new2,
                    key3, old3, new3,
                    count: 4),
                IsActive = true
            };
        }

        if (!isEditing)
        {
            CommitPendingEdit();
            return;
        }

        NotifyPropertyWidgetState(widgetId, isEditing);
    }

    public void SetPropertyBatch4(
        int widgetId,
        bool isEditing,
        EntityId editedEntity,
        PropertySlot slot0,
        in PropertyValue new0,
        PropertySlot slot1,
        in PropertyValue new1,
        PropertySlot slot2,
        in PropertyValue new2,
        PropertySlot slot3,
        in PropertyValue new3)
    {
        if (!TryResolveSlot(PropertyKey.FromSlot(slot0), out PropertySlot resolved0) ||
            !TryResolveSlot(PropertyKey.FromSlot(slot1), out PropertySlot resolved1) ||
            !TryResolveSlot(PropertyKey.FromSlot(slot2), out PropertySlot resolved2) ||
            !TryResolveSlot(PropertyKey.FromSlot(slot3), out PropertySlot resolved3))
        {
            return;
        }

        PropertyValue old0 = ReadValue(_workspace.PropertyWorld, resolved0);
        PropertyValue old1 = ReadValue(_workspace.PropertyWorld, resolved1);
        PropertyValue old2 = ReadValue(_workspace.PropertyWorld, resolved2);
        PropertyValue old3 = ReadValue(_workspace.PropertyWorld, resolved3);

        bool changed0 = !old0.Equals(resolved0.Kind, new0);
        bool changed1 = !old1.Equals(resolved1.Kind, new1);
        bool changed2 = !old2.Equals(resolved2.Kind, new2);
        bool changed3 = !old3.Equals(resolved3.Kind, new3);
        if (!changed0 && !changed1 && !changed2 && !changed3)
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        if (TryApplyAnimationSelectionPropertyBatch4(widgetId, isEditing, editedEntity, resolved0, old0, new0, resolved1, old1, new1, resolved2, old2, new2, resolved3, old3, new3))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        Span<PropertySlot> resolvedSlots = stackalloc PropertySlot[4] { resolved0, resolved1, resolved2, resolved3 };
        Span<PropertyValue> oldValues = stackalloc PropertyValue[4] { old0, old1, old2, old3 };
        Span<PropertyValue> newValues = stackalloc PropertyValue[4] { new0, new1, new2, new3 };
        if (TryApplyPrefabExpandedPropertyEdits(widgetId, isEditing, editedEntity, resolvedSlots, oldValues, newValues))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }
        if (TryApplyPrefabInstanceRootLayoutEdits(widgetId, isEditing, editedEntity, resolvedSlots, oldValues, newValues))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        if (changed0)
        {
            WriteValue(_workspace.PropertyWorld, resolved0, new0);
            AutoKeyIfTimelineSelected(resolved0, old0, new0);
        }
        if (changed1)
        {
            WriteValue(_workspace.PropertyWorld, resolved1, new1);
            AutoKeyIfTimelineSelected(resolved1, old1, new1);
        }
        if (changed2)
        {
            WriteValue(_workspace.PropertyWorld, resolved2, new2);
            AutoKeyIfTimelineSelected(resolved2, old2, new2);
        }
        if (changed3)
        {
            WriteValue(_workspace.PropertyWorld, resolved3, new3);
            AutoKeyIfTimelineSelected(resolved3, old3, new3);
        }

        PropertyKey key0 = PropertyKey.FromSlot(resolved0);
        PropertyKey key1 = PropertyKey.FromSlot(resolved1);
        PropertyKey key2 = PropertyKey.FromSlot(resolved2);
        PropertyKey key3 = PropertyKey.FromSlot(resolved3);

        if (_pendingEdit.IsActive && _pendingEdit.WidgetId == widgetId && _pendingEdit.PendingRecord.Kind == UndoRecordKind.SetPropertyBatch &&
            _pendingEdit.PendingRecord.PropertyCount == 4 &&
            _pendingEdit.PendingRecord.Property0.PropertyId == key0.PropertyId &&
            _pendingEdit.PendingRecord.Property1.PropertyId == key1.PropertyId &&
            _pendingEdit.PendingRecord.Property2.PropertyId == key2.PropertyId &&
            _pendingEdit.PendingRecord.Property3.PropertyId == key3.PropertyId &&
            _pendingEdit.PendingRecord.Property0.Component.Equals(key0.Component) &&
            _pendingEdit.PendingRecord.Property1.Component.Equals(key1.Component) &&
            _pendingEdit.PendingRecord.Property2.Component.Equals(key2.Component) &&
            _pendingEdit.PendingRecord.Property3.Component.Equals(key3.Component))
        {
            _pendingEdit.PendingRecord.After0 = new0;
            _pendingEdit.PendingRecord.After1 = new1;
            _pendingEdit.PendingRecord.After2 = new2;
            _pendingEdit.PendingRecord.After3 = new3;
        }
        else
        {
            if (_pendingEdit.IsActive)
            {
                CommitPendingEdit();
            }

            _pendingEdit = new PendingEdit
            {
                WidgetId = widgetId,
                PendingRecord = UndoRecord.ForPropertyBatch(
                    key0, old0, new0,
                    key1, old1, new1,
                    key2, old2, new2,
                    key3, old3, new3,
                    count: 4),
                IsActive = true
            };
        }

        if (!isEditing)
        {
            CommitPendingEdit();
            return;
        }

        NotifyPropertyWidgetState(widgetId, isEditing);
    }

    private bool TryApplyPrefabExpandedPropertyEdits(
        int widgetId,
        bool isEditing,
        EntityId editedEntity,
        ReadOnlySpan<PropertySlot> resolvedSlots,
        ReadOnlySpan<PropertyValue> oldValues,
        ReadOnlySpan<PropertyValue> newValues)
    {
        if (editedEntity.IsNull)
        {
            return false;
        }

        if (resolvedSlots.IsEmpty || oldValues.Length != resolvedSlots.Length || newValues.Length != resolvedSlots.Length)
        {
            return false;
        }

        if (!_workspace.World.TryGetComponent(editedEntity, PrefabExpandedComponent.Api.PoolIdConst, out AnyComponentHandle expandedAny) || !expandedAny.IsValid)
        {
            return false;
        }

        var expandedHandle = new PrefabExpandedComponentHandle(expandedAny.Index, expandedAny.Generation);
        var expanded = PrefabExpandedComponent.Api.FromHandle(_workspace.PropertyWorld, expandedHandle);
        if (!expanded.IsAlive || expanded.InstanceRootStableId == 0 || expanded.SourceNodeStableId == 0)
        {
            return false;
        }

        EntityId instanceRootEntity = _workspace.World.GetEntityByStableId(expanded.InstanceRootStableId);
        if (instanceRootEntity.IsNull || _workspace.World.GetNodeType(instanceRootEntity) != UiNodeType.PrefabInstance)
        {
            return false;
        }

        uint sourcePrefabStableId = 0;
        if (_workspace.World.TryGetComponent(instanceRootEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) && instanceAny.IsValid)
        {
            var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var instance = PrefabInstanceComponent.Api.FromHandle(_workspace.PropertyWorld, instanceHandle);
            if (instance.IsAlive)
            {
                sourcePrefabStableId = instance.SourcePrefabStableId;
            }
        }

        if (sourcePrefabStableId == 0)
        {
            return false;
        }

        uint editedStableId = _workspace.World.GetStableId(editedEntity);
        if (editedStableId == 0)
        {
            return false;
        }

        if (!TryEnsurePrefabInstancePropertyOverridesComponent(instanceRootEntity, out AnyComponentHandle overridesAny) || !overridesAny.IsValid)
        {
            return false;
        }

        var overrideHandle = new PrefabInstancePropertyOverridesComponentHandle(overridesAny.Index, overridesAny.Generation);
        var overrides = PrefabInstancePropertyOverridesComponent.Api.FromHandle(_workspace.PropertyWorld, overrideHandle);
        if (!overrides.IsAlive)
        {
            return false;
        }

        Span<AnyComponentHandle> uniqueComponents = stackalloc AnyComponentHandle[8];
        int uniqueCount = 0;
        for (int i = 0; i < resolvedSlots.Length; i++)
        {
            AnyComponentHandle component = resolvedSlots[i].Component;
            if (!component.IsValid)
            {
                continue;
            }

            bool exists = false;
            for (int j = 0; j < uniqueCount; j++)
            {
                if (uniqueComponents[j].Kind == component.Kind)
                {
                    exists = true;
                    break;
                }
            }

            if (exists)
            {
                continue;
            }

            if (uniqueCount >= uniqueComponents.Length)
            {
                break;
            }

            uniqueComponents[uniqueCount++] = component;
        }

        Span<ComponentSnapshotTarget> targets = stackalloc ComponentSnapshotTarget[10];
        int targetCount = 0;
        for (int i = 0; i < uniqueCount; i++)
        {
            targets[targetCount++] = new ComponentSnapshotTarget(editedStableId, uniqueComponents[i]);
        }
        targets[targetCount++] = new ComponentSnapshotTarget(expanded.InstanceRootStableId, overridesAny);

        int payloadStart = BeginOrUpdateComponentSnapshotManyEdit(widgetId, targets.Slice(0, targetCount));
        if (payloadStart < 0)
        {
            return false;
        }

        EntityId sourceEntity = _workspace.World.GetEntityByStableId(expanded.SourceNodeStableId);

        for (int i = 0; i < resolvedSlots.Length; i++)
        {
            if (oldValues[i].Equals(resolvedSlots[i].Kind, newValues[i]))
            {
                continue;
            }

            WriteValue(_workspace.PropertyWorld, resolvedSlots[i], newValues[i]);
            AutoKeyIfTimelineSelected(resolvedSlots[i], oldValues[i], newValues[i]);
            UpdatePrefabExpandedOverrideEntry(overrides, sourceEntity, sourcePrefabStableId, expanded.SourceNodeStableId, resolvedSlots[i], newValues[i]);
        }

        UpdateComponentSnapshotManyAfter(payloadStart, targets.Slice(0, targetCount));

        if (!isEditing)
        {
            CommitPendingEdit();
        }

        return true;
    }

    private bool TryApplyPrefabInstanceRootLayoutEdits(
        int widgetId,
        bool isEditing,
        EntityId editedEntity,
        ReadOnlySpan<PropertySlot> resolvedSlots,
        ReadOnlySpan<PropertyValue> oldValues,
        ReadOnlySpan<PropertyValue> newValues)
    {
        if (editedEntity.IsNull)
        {
            return false;
        }

        if (resolvedSlots.IsEmpty || oldValues.Length != resolvedSlots.Length || newValues.Length != resolvedSlots.Length)
        {
            return false;
        }

        if (_workspace.World.GetNodeType(editedEntity) != UiNodeType.PrefabInstance)
        {
            return false;
        }

        if (_workspace.World.TryGetComponent(editedEntity, PrefabExpandedComponent.Api.PoolIdConst, out AnyComponentHandle expandedAny) && expandedAny.IsValid)
        {
            // Expanded entities are handled separately.
            return false;
        }

        if (!_workspace.World.TryGetComponent(editedEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) || !instanceAny.IsValid)
        {
            return false;
        }

        var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
        var instance = PrefabInstanceComponent.Api.FromHandle(_workspace.PropertyWorld, instanceHandle);
        if (!instance.IsAlive || instance.SourcePrefabStableId == 0)
        {
            return false;
        }

        EntityId sourcePrefabEntity = _workspace.World.GetEntityByStableId(instance.SourcePrefabStableId);
        if (sourcePrefabEntity.IsNull || _workspace.World.GetNodeType(sourcePrefabEntity) != UiNodeType.Prefab)
        {
            return false;
        }

        bool hasAnyLayoutChildEdits = false;
        StringHandle layoutChildGroup = "Layout Child";
        for (int i = 0; i < resolvedSlots.Length; i++)
        {
            if (resolvedSlots[i].Component.Kind != TransformComponent.Api.PoolIdConst)
            {
                continue;
            }

            if (!PropertyDispatcher.TryGetInfo(resolvedSlots[i].Component, resolvedSlots[i].PropertyIndex, out PropertyInfo info))
            {
                continue;
            }

            if (info.Group == layoutChildGroup)
            {
                hasAnyLayoutChildEdits = true;
                break;
            }
        }

        if (!hasAnyLayoutChildEdits)
        {
            return false;
        }

        uint editedStableId = _workspace.World.GetStableId(editedEntity);
        if (editedStableId == 0)
        {
            return false;
        }

        if (!TryEnsurePrefabInstancePropertyOverridesComponent(editedEntity, out AnyComponentHandle overridesAny) || !overridesAny.IsValid)
        {
            return false;
        }

        var overrideHandle = new PrefabInstancePropertyOverridesComponentHandle(overridesAny.Index, overridesAny.Generation);
        var overrides = PrefabInstancePropertyOverridesComponent.Api.FromHandle(_workspace.PropertyWorld, overrideHandle);
        if (!overrides.IsAlive)
        {
            return false;
        }

        Span<AnyComponentHandle> uniqueComponents = stackalloc AnyComponentHandle[8];
        int uniqueCount = 0;
        for (int i = 0; i < resolvedSlots.Length; i++)
        {
            AnyComponentHandle component = resolvedSlots[i].Component;
            if (!component.IsValid)
            {
                continue;
            }

            bool exists = false;
            for (int j = 0; j < uniqueCount; j++)
            {
                if (uniqueComponents[j].Kind == component.Kind)
                {
                    exists = true;
                    break;
                }
            }

            if (exists)
            {
                continue;
            }

            if (uniqueCount >= uniqueComponents.Length)
            {
                break;
            }

            uniqueComponents[uniqueCount++] = component;
        }

        Span<ComponentSnapshotTarget> targets = stackalloc ComponentSnapshotTarget[10];
        int targetCount = 0;
        for (int i = 0; i < uniqueCount; i++)
        {
            targets[targetCount++] = new ComponentSnapshotTarget(editedStableId, uniqueComponents[i]);
        }
        targets[targetCount++] = new ComponentSnapshotTarget(editedStableId, overridesAny);

        int payloadStart = BeginOrUpdateComponentSnapshotManyEdit(widgetId, targets.Slice(0, targetCount));
        if (payloadStart < 0)
        {
            return false;
        }

        uint sourcePrefabStableId = instance.SourcePrefabStableId;

        for (int i = 0; i < resolvedSlots.Length; i++)
        {
            if (oldValues[i].Equals(resolvedSlots[i].Kind, newValues[i]))
            {
                continue;
            }

            WriteValue(_workspace.PropertyWorld, resolvedSlots[i], newValues[i]);
            AutoKeyIfTimelineSelected(resolvedSlots[i], oldValues[i], newValues[i]);

            if (resolvedSlots[i].Component.Kind != TransformComponent.Api.PoolIdConst)
            {
                continue;
            }

            if (!PropertyDispatcher.TryGetInfo(resolvedSlots[i].Component, resolvedSlots[i].PropertyIndex, out PropertyInfo info))
            {
                continue;
            }

            if (info.Group != layoutChildGroup)
            {
                continue;
            }

            UpdatePrefabExpandedOverrideEntry(overrides, sourcePrefabEntity, sourcePrefabStableId, sourcePrefabStableId, resolvedSlots[i], newValues[i]);
        }

        UpdateComponentSnapshotManyAfter(payloadStart, targets.Slice(0, targetCount));

        if (!isEditing)
        {
            CommitPendingEdit();
        }

        return true;
    }

    private void UpdatePrefabExpandedOverrideEntry(
        PrefabInstancePropertyOverridesComponent.ViewProxy overrides,
        EntityId sourceEntity,
        uint sourcePrefabStableId,
        uint sourceNodeStableId,
        in PropertySlot resolvedSlot,
        in PropertyValue value)
    {
        ushort componentKind = resolvedSlot.Component.Kind;
        ulong propertyId = resolvedSlot.PropertyId;
        ushort propertyIndexHint = (ushort)resolvedSlot.PropertyIndex;
        ushort propertyKind = (ushort)resolvedSlot.Kind;

        bool matchesSource = false;
        if (!sourceEntity.IsNull &&
            _workspace.World.TryGetComponent(sourceEntity, componentKind, out AnyComponentHandle sourceComponent) &&
            sourceComponent.IsValid &&
            TryResolveSlotForOverride(sourceComponent, propertyIndexHint, propertyId, resolvedSlot.Kind, out PropertySlot sourceSlot))
        {
            PropertyValue sourceValue = ReadValue(_workspace.PropertyWorld, sourceSlot);
            matchesSource = sourceValue.Equals(resolvedSlot.Kind, value);
        }

        bool isRoot = sourceNodeStableId == sourcePrefabStableId;

        Span<uint> srcIds = overrides.SourceNodeStableIdSpan();
        Span<ushort> kinds = overrides.ComponentKindSpan();
        Span<ushort> hint = overrides.PropertyIndexHintSpan();
        Span<ulong> ids = overrides.PropertyIdSpan();
        Span<ushort> propKinds = overrides.PropertyKindSpan();
        Span<PropertyValue> values = overrides.ValueSpan();

        int count = overrides.Count;
        if (count > PrefabInstancePropertyOverridesComponent.MaxOverrides)
        {
            count = PrefabInstancePropertyOverridesComponent.MaxOverrides;
        }

        int rootCount = overrides.RootOverrideCount;
        if (rootCount > count)
        {
            rootCount = count;
        }

        int searchStart = isRoot ? 0 : rootCount;
        int searchEnd = isRoot ? rootCount : count;

        int existingIndex = -1;
        for (int i = searchStart; i < searchEnd; i++)
        {
            if (srcIds[i] == sourceNodeStableId && kinds[i] == componentKind && ids[i] == propertyId)
            {
                existingIndex = i;
                break;
            }
        }

        if (matchesSource)
        {
            if (existingIndex >= 0)
            {
                RemoveOverrideEntry(overrides, existingIndex);
            }

            return;
        }

        if (existingIndex >= 0)
        {
            hint[existingIndex] = propertyIndexHint;
            propKinds[existingIndex] = propertyKind;
            values[existingIndex] = value;
            return;
        }

        if (count >= PrefabInstancePropertyOverridesComponent.MaxOverrides)
        {
            _workspace.ShowToast("Prefab override list full");
            return;
        }

        int insertIndex = isRoot ? rootCount : count;
        InsertOverrideEntry(overrides, insertIndex);

        srcIds[insertIndex] = sourceNodeStableId;
        kinds[insertIndex] = componentKind;
        hint[insertIndex] = propertyIndexHint;
        ids[insertIndex] = propertyId;
        propKinds[insertIndex] = propertyKind;
        values[insertIndex] = value;
    }

    private static bool TryResolveSlotForOverride(AnyComponentHandle component, ushort propertyIndexHint, ulong propertyId, PropertyKind kind, out PropertySlot slot)
    {
        slot = default;

        int propertyCount = PropertyDispatcher.GetPropertyCount(component);
        if (propertyCount <= 0)
        {
            return false;
        }

        int hintIndex = propertyIndexHint;
        if (hintIndex >= 0 && hintIndex < propertyCount)
        {
            if (PropertyDispatcher.TryGetInfo(component, hintIndex, out var hintInfo) && hintInfo.PropertyId == propertyId && hintInfo.Kind == kind)
            {
                slot = PropertyDispatcher.GetSlot(component, hintIndex);
                return true;
            }
        }

        for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
        {
            if (!PropertyDispatcher.TryGetInfo(component, propertyIndex, out var info))
            {
                continue;
            }

            if (info.PropertyId != propertyId || info.Kind != kind)
            {
                continue;
            }

            slot = PropertyDispatcher.GetSlot(component, propertyIndex);
            return true;
        }

        return false;
    }

    private static void RemoveOverrideEntry(PrefabInstancePropertyOverridesComponent.ViewProxy overrides, int removeIndex)
    {
        int count = overrides.Count;
        if (count == 0)
        {
            return;
        }

        int rootCount = overrides.RootOverrideCount;
        if (rootCount > count)
        {
            rootCount = count;
        }

        Span<uint> srcIds = overrides.SourceNodeStableIdSpan();
        Span<ushort> kinds = overrides.ComponentKindSpan();
        Span<ushort> hint = overrides.PropertyIndexHintSpan();
        Span<ulong> ids = overrides.PropertyIdSpan();
        Span<ushort> propKinds = overrides.PropertyKindSpan();
        Span<PropertyValue> values = overrides.ValueSpan();

        for (int i = removeIndex; i < count - 1; i++)
        {
            srcIds[i] = srcIds[i + 1];
            kinds[i] = kinds[i + 1];
            hint[i] = hint[i + 1];
            ids[i] = ids[i + 1];
            propKinds[i] = propKinds[i + 1];
            values[i] = values[i + 1];
        }

        int last = count - 1;
        srcIds[last] = 0;
        kinds[last] = 0;
        hint[last] = 0;
        ids[last] = 0;
        propKinds[last] = 0;
        values[last] = default;

        overrides.Count = (ushort)(count - 1);
        if (removeIndex < rootCount)
        {
            overrides.RootOverrideCount = (ushort)(rootCount - 1);
        }
    }

    private static void InsertOverrideEntry(PrefabInstancePropertyOverridesComponent.ViewProxy overrides, int insertIndex)
    {
        int count = overrides.Count;
        if (count >= PrefabInstancePropertyOverridesComponent.MaxOverrides)
        {
            return;
        }

        Span<uint> srcIds = overrides.SourceNodeStableIdSpan();
        Span<ushort> kinds = overrides.ComponentKindSpan();
        Span<ushort> hint = overrides.PropertyIndexHintSpan();
        Span<ulong> ids = overrides.PropertyIdSpan();
        Span<ushort> propKinds = overrides.PropertyKindSpan();
        Span<PropertyValue> values = overrides.ValueSpan();

        for (int i = count; i > insertIndex; i--)
        {
            srcIds[i] = srcIds[i - 1];
            kinds[i] = kinds[i - 1];
            hint[i] = hint[i - 1];
            ids[i] = ids[i - 1];
            propKinds[i] = propKinds[i - 1];
            values[i] = values[i - 1];
        }

        srcIds[insertIndex] = 0;
        kinds[insertIndex] = 0;
        hint[insertIndex] = 0;
        ids[insertIndex] = 0;
        propKinds[insertIndex] = 0;
        values[insertIndex] = default;

        overrides.Count = (ushort)(count + 1);
        int rootCount = overrides.RootOverrideCount;
        if (insertIndex <= rootCount)
        {
            overrides.RootOverrideCount = (ushort)(rootCount + 1);
        }
    }

    public void SetPropertyValuesMany(int widgetId, bool isEditing, ReadOnlySpan<PropertySlot> slots, ReadOnlySpan<PropertyValue> newValues)
    {
        if (slots.IsEmpty)
        {
            return;
        }

        bool broadcastValue = newValues.Length == 1;
        if (!broadcastValue && newValues.Length != slots.Length)
        {
            return;
        }

        int payloadCount = slots.Length;

        bool canUpdatePending =
            _pendingEdit.IsActive &&
            _pendingEdit.WidgetId == widgetId &&
            _pendingEdit.PendingRecord.Kind == UndoRecordKind.SetPropertyMany &&
            _pendingEdit.PendingRecord.PayloadCount == payloadCount &&
            _pendingEdit.PendingRecord.PayloadStart >= 0 &&
            _pendingEdit.PendingRecord.PayloadStart + _pendingEdit.PendingRecord.PayloadCount <= _undoPropertyPayload.Count;

        if (canUpdatePending)
        {
            int start = _pendingEdit.PendingRecord.PayloadStart;
            PropertyKey expected = PropertyKey.FromSlot(slots[0]);
            PropertyKey existing = _undoPropertyPayload[start].Key;
            if (!existing.Component.Equals(expected.Component) || existing.PropertyId != expected.PropertyId || existing.Kind != expected.Kind)
            {
                canUpdatePending = false;
            }
        }

        if (!canUpdatePending && _pendingEdit.IsActive)
        {
            CommitPendingEdit();
        }

        if (!canUpdatePending)
        {
            int payloadStart = _undoPropertyPayload.Count;
            _pendingEdit = new PendingEdit
            {
                WidgetId = widgetId,
                PendingRecord = UndoRecord.ForPropertyMany(payloadStart, payloadCount),
                IsActive = true
            };

            for (int i = 0; i < payloadCount; i++)
            {
                PropertyValue after = broadcastValue ? newValues[0] : newValues[i];
                if (!TryResolveSlot(PropertyKey.FromSlot(slots[i]), out PropertySlot resolved))
                {
                    _undoPropertyPayload.Add(new PropertyUndoPayload
                    {
                        Key = default,
                        Before = default,
                        After = after
                    });
                    continue;
                }

                PropertyValue before = ReadValue(_workspace.PropertyWorld, resolved);
                if (!before.Equals(resolved.Kind, after))
                {
                    WriteValue(_workspace.PropertyWorld, resolved, after);
                    AutoKeyIfTimelineSelected(resolved, before, after);
                }

                _undoPropertyPayload.Add(new PropertyUndoPayload
                {
                    Key = PropertyKey.FromSlot(resolved),
                    Before = before,
                    After = after
                });
            }
        }
        else
        {
            int payloadStart = _pendingEdit.PendingRecord.PayloadStart;
            Span<PropertyUndoPayload> payload = CollectionsMarshal.AsSpan(_undoPropertyPayload);
            for (int i = 0; i < payloadCount; i++)
            {
                PropertyValue after = broadcastValue ? newValues[0] : newValues[i];

                if (TryResolveSlot(PropertyKey.FromSlot(slots[i]), out PropertySlot resolved))
                {
                    PropertyValue current = ReadValue(_workspace.PropertyWorld, resolved);
                    if (!current.Equals(resolved.Kind, after))
                    {
                        WriteValue(_workspace.PropertyWorld, resolved, after);
                        AutoKeyIfTimelineSelected(resolved, current, after);
                    }
                }

                PropertyUndoPayload entry = payload[payloadStart + i];
                entry.After = after;
                payload[payloadStart + i] = entry;
            }
        }

        if (!isEditing)
        {
            CommitPendingEdit();
            return;
        }

        NotifyPropertyWidgetState(widgetId, isEditing);
    }

    private void AutoKeyIfTimelineSelected(PropertySlot slot, in PropertyValue oldValue, in PropertyValue newValue)
    {
        if (!_workspace.TryGetActiveAnimationDocument(out var animations) || !animations.TryGetSelectedTimeline(out var timeline) || timeline == null)
        {
            return;
        }

        if (!AnimationEditorHelpers.TryGetSelectionTargetStableId(_workspace, out uint targetStableId))
        {
            return;
        }

        OnPropertyEditedWhileTimelineSelected(targetStableId, slot, oldValue, newValue, timeline);
    }

    private bool TryApplyAnimationSelectionPropertyEdit(
        int widgetId,
        bool isEditing,
        EntityId editedEntity,
        PropertySlot slot,
        in PropertyValue oldValue,
        in PropertyValue newValue)
    {
        _ = oldValue;

        ushort componentKind = slot.Component.Kind;
        if (componentKind != AnimationTimelineSelectionComponent.Api.PoolIdConst &&
            componentKind != AnimationKeyframeSelectionFloatComponent.Api.PoolIdConst &&
            componentKind != AnimationKeyframeSelectionIntComponent.Api.PoolIdConst &&
            componentKind != AnimationKeyframeSelectionBoolComponent.Api.PoolIdConst &&
            componentKind != AnimationKeyframeSelectionVec2Component.Api.PoolIdConst &&
            componentKind != AnimationKeyframeSelectionVec3Component.Api.PoolIdConst &&
            componentKind != AnimationKeyframeSelectionVec4Component.Api.PoolIdConst &&
            componentKind != AnimationKeyframeSelectionColor32Component.Api.PoolIdConst)
        {
            return false;
        }

        if (!AnimationEditorWindow.IsTimelineMode())
        {
            return false;
        }

        if (editedEntity.IsNull)
        {
            return false;
        }

        uint stableId = _workspace.World.GetStableId(editedEntity);
        if (stableId == 0)
        {
            return false;
        }

        if (!_workspace.World.TryGetComponent(editedEntity, AnimationLibraryComponent.Api.PoolIdConst, out AnyComponentHandle libraryAny) || !libraryAny.IsValid)
        {
            if (!_workspace.Commands.TryEnsureAnimationLibraryComponent(editedEntity, out libraryAny) || !libraryAny.IsValid)
            {
                return false;
            }
        }

        if (!_workspace.TryGetActiveAnimationDocument(out var doc) || doc == null)
        {
            return false;
        }

        int payloadIndex = BeginOrUpdateComponentSnapshotEdit(widgetId, stableId, libraryAny);
        if (payloadIndex < 0)
        {
            return false;
        }

        bool applied = componentKind switch
        {
            AnimationTimelineSelectionComponent.Api.PoolIdConst => TryApplyTimelineSelectionPropertyEdit(doc, slot, newValue),
            _ => TryApplyKeyframeSelectionPropertyEdit(doc, slot, newValue)
        };

        if (!applied)
        {
            return false;
        }

        _workspace.SyncActiveAnimationDocumentToLibrary();
        UpdateComponentSnapshotAfter(payloadIndex, libraryAny);
        AnimationEditorWindow.MarkPreviewDirty();

        if (!isEditing)
        {
            CommitPendingEdit();
        }

        return true;
    }

    private bool TryApplyAnimationSelectionPropertyBatch2(
        int widgetId,
        bool isEditing,
        EntityId editedEntity,
        PropertySlot slot0,
        in PropertyValue old0,
        in PropertyValue new0,
        PropertySlot slot1,
        in PropertyValue old1,
        in PropertyValue new1)
    {
        _ = old0;
        _ = old1;

        if (slot0.Component.IsNull || slot1.Component.IsNull)
        {
            return false;
        }

        if (!slot0.Component.Equals(slot1.Component))
        {
            return false;
        }

        if (slot0.Component.Kind != AnimationKeyframeSelectionVec2Component.Api.PoolIdConst)
        {
            return false;
        }

        ulong xId = AnimationKeyframeSelectionVec2ComponentProperties.ValueXId;
        ulong yId = AnimationKeyframeSelectionVec2ComponentProperties.ValueYId;

        bool slot0IsX = slot0.PropertyId == xId;
        bool slot0IsY = slot0.PropertyId == yId;
        bool slot1IsX = slot1.PropertyId == xId;
        bool slot1IsY = slot1.PropertyId == yId;

        if (!(slot0IsX || slot0IsY) || !(slot1IsX || slot1IsY) || slot0IsX == slot1IsX)
        {
            return false;
        }

        float x = slot0IsX ? new0.Float : new1.Float;
        float y = slot0IsY ? new0.Float : new1.Float;
        return TryApplyAnimationSelectionKeyframeValueOverride(
            widgetId,
            isEditing,
            editedEntity,
            slot0.Component,
            PropertyValue.FromVec2(new System.Numerics.Vector2(x, y)));
    }

    private bool TryApplyAnimationSelectionPropertyBatch3(
        int widgetId,
        bool isEditing,
        EntityId editedEntity,
        PropertySlot slot0,
        in PropertyValue old0,
        in PropertyValue new0,
        PropertySlot slot1,
        in PropertyValue old1,
        in PropertyValue new1,
        PropertySlot slot2,
        in PropertyValue old2,
        in PropertyValue new2)
    {
        _ = old0;
        _ = old1;
        _ = old2;

        if (slot0.Component.IsNull || slot1.Component.IsNull || slot2.Component.IsNull)
        {
            return false;
        }

        if (!slot0.Component.Equals(slot1.Component) || !slot0.Component.Equals(slot2.Component))
        {
            return false;
        }

        if (slot0.Component.Kind != AnimationKeyframeSelectionVec3Component.Api.PoolIdConst)
        {
            return false;
        }

        ulong xId = AnimationKeyframeSelectionVec3ComponentProperties.ValueXId;
        ulong yId = AnimationKeyframeSelectionVec3ComponentProperties.ValueYId;
        ulong zId = AnimationKeyframeSelectionVec3ComponentProperties.ValueZId;

        if (!TryReadChannelValue3(slot0, new0, slot1, new1, slot2, new2, xId, yId, zId, out float x, out float y, out float z))
        {
            return false;
        }

        return TryApplyAnimationSelectionKeyframeValueOverride(
            widgetId,
            isEditing,
            editedEntity,
            slot0.Component,
            PropertyValue.FromVec3(new System.Numerics.Vector3(x, y, z)));
    }

    private bool TryApplyAnimationSelectionPropertyBatch4(
        int widgetId,
        bool isEditing,
        EntityId editedEntity,
        PropertySlot slot0,
        in PropertyValue old0,
        in PropertyValue new0,
        PropertySlot slot1,
        in PropertyValue old1,
        in PropertyValue new1,
        PropertySlot slot2,
        in PropertyValue old2,
        in PropertyValue new2,
        PropertySlot slot3,
        in PropertyValue old3,
        in PropertyValue new3)
    {
        _ = old0;
        _ = old1;
        _ = old2;
        _ = old3;

        if (slot0.Component.IsNull || slot1.Component.IsNull || slot2.Component.IsNull || slot3.Component.IsNull)
        {
            return false;
        }

        if (!slot0.Component.Equals(slot1.Component) || !slot0.Component.Equals(slot2.Component) || !slot0.Component.Equals(slot3.Component))
        {
            return false;
        }

        if (slot0.Component.Kind != AnimationKeyframeSelectionVec4Component.Api.PoolIdConst)
        {
            return false;
        }

        ulong xId = AnimationKeyframeSelectionVec4ComponentProperties.ValueXId;
        ulong yId = AnimationKeyframeSelectionVec4ComponentProperties.ValueYId;
        ulong zId = AnimationKeyframeSelectionVec4ComponentProperties.ValueZId;
        ulong wId = AnimationKeyframeSelectionVec4ComponentProperties.ValueWId;

        if (!TryReadChannelValue4(slot0, new0, slot1, new1, slot2, new2, slot3, new3, xId, yId, zId, wId, out float x, out float y, out float z, out float w))
        {
            return false;
        }

        return TryApplyAnimationSelectionKeyframeValueOverride(
            widgetId,
            isEditing,
            editedEntity,
            slot0.Component,
            PropertyValue.FromVec4(new System.Numerics.Vector4(x, y, z, w)));
    }

    private static bool TryReadChannelValue3(
        PropertySlot slot0,
        in PropertyValue value0,
        PropertySlot slot1,
        in PropertyValue value1,
        PropertySlot slot2,
        in PropertyValue value2,
        ulong xId,
        ulong yId,
        ulong zId,
        out float x,
        out float y,
        out float z)
    {
        x = 0f;
        y = 0f;
        z = 0f;

        bool hasX = false;
        bool hasY = false;
        bool hasZ = false;

        if (slot0.PropertyId == xId) { x = value0.Float; hasX = true; }
        else if (slot0.PropertyId == yId) { y = value0.Float; hasY = true; }
        else if (slot0.PropertyId == zId) { z = value0.Float; hasZ = true; }

        if (slot1.PropertyId == xId) { x = value1.Float; hasX = true; }
        else if (slot1.PropertyId == yId) { y = value1.Float; hasY = true; }
        else if (slot1.PropertyId == zId) { z = value1.Float; hasZ = true; }

        if (slot2.PropertyId == xId) { x = value2.Float; hasX = true; }
        else if (slot2.PropertyId == yId) { y = value2.Float; hasY = true; }
        else if (slot2.PropertyId == zId) { z = value2.Float; hasZ = true; }

        return hasX && hasY && hasZ;
    }

    private static bool TryReadChannelValue4(
        PropertySlot slot0,
        in PropertyValue value0,
        PropertySlot slot1,
        in PropertyValue value1,
        PropertySlot slot2,
        in PropertyValue value2,
        PropertySlot slot3,
        in PropertyValue value3,
        ulong xId,
        ulong yId,
        ulong zId,
        ulong wId,
        out float x,
        out float y,
        out float z,
        out float w)
    {
        x = 0f;
        y = 0f;
        z = 0f;
        w = 0f;

        bool hasX = false;
        bool hasY = false;
        bool hasZ = false;
        bool hasW = false;

        if (slot0.PropertyId == xId) { x = value0.Float; hasX = true; }
        else if (slot0.PropertyId == yId) { y = value0.Float; hasY = true; }
        else if (slot0.PropertyId == zId) { z = value0.Float; hasZ = true; }
        else if (slot0.PropertyId == wId) { w = value0.Float; hasW = true; }

        if (slot1.PropertyId == xId) { x = value1.Float; hasX = true; }
        else if (slot1.PropertyId == yId) { y = value1.Float; hasY = true; }
        else if (slot1.PropertyId == zId) { z = value1.Float; hasZ = true; }
        else if (slot1.PropertyId == wId) { w = value1.Float; hasW = true; }

        if (slot2.PropertyId == xId) { x = value2.Float; hasX = true; }
        else if (slot2.PropertyId == yId) { y = value2.Float; hasY = true; }
        else if (slot2.PropertyId == zId) { z = value2.Float; hasZ = true; }
        else if (slot2.PropertyId == wId) { w = value2.Float; hasW = true; }

        if (slot3.PropertyId == xId) { x = value3.Float; hasX = true; }
        else if (slot3.PropertyId == yId) { y = value3.Float; hasY = true; }
        else if (slot3.PropertyId == zId) { z = value3.Float; hasZ = true; }
        else if (slot3.PropertyId == wId) { w = value3.Float; hasW = true; }

        return hasX && hasY && hasZ && hasW;
    }

    private bool TryApplyAnimationSelectionKeyframeValueOverride(
        int widgetId,
        bool isEditing,
        EntityId editedEntity,
        AnyComponentHandle selectionComponent,
        in PropertyValue newValue)
    {
        if (editedEntity.IsNull)
        {
            return false;
        }

        if (!AnimationEditorWindow.IsTimelineMode())
        {
            return false;
        }

        uint stableId = _workspace.World.GetStableId(editedEntity);
        if (stableId == 0)
        {
            return false;
        }

        if (!_workspace.World.TryGetComponent(editedEntity, AnimationLibraryComponent.Api.PoolIdConst, out AnyComponentHandle libraryAny) || !libraryAny.IsValid)
        {
            if (!TryEnsureAnimationLibraryComponent(editedEntity, out libraryAny) || !libraryAny.IsValid)
            {
                return false;
            }
        }

        if (!_workspace.TryGetActiveAnimationDocument(out var doc) || doc == null)
        {
            return false;
        }

        int payloadIndex = BeginOrUpdateComponentSnapshotEdit(widgetId, stableId, libraryAny);
        if (payloadIndex < 0)
        {
            return false;
        }

        if (!TryReadKeySelection(_workspace.PropertyWorld, selectionComponent, out int timelineId, out int keyIndex, out AnimationDocument.AnimationBinding binding))
        {
            return false;
        }

        if (!doc.TryGetTimelineById(timelineId, out var timeline) || timeline == null)
        {
            return false;
        }

        AnimationDocument.AnimationTrack? track = AnimationEditorHelpers.FindTrack(timeline, binding);
        if (track == null)
        {
            return false;
        }

        if ((uint)keyIndex >= (uint)track.Keys.Count)
        {
            return false;
        }

        var key = track.Keys[keyIndex];
        key.Value = newValue;
        track.Keys[keyIndex] = key;

        _workspace.SyncActiveAnimationDocumentToLibrary();
        UpdateComponentSnapshotAfter(payloadIndex, libraryAny);
        AnimationEditorWindow.MarkPreviewDirty();

        if (!isEditing)
        {
            CommitPendingEdit();
        }

        return true;
    }

    private static bool TryApplyTimelineSelectionPropertyEdit(AnimationDocument doc, PropertySlot slot, in PropertyValue newValue)
    {
        if (doc == null)
        {
            return false;
        }

        if (!doc.TryGetSelectedTimeline(out var timeline) || timeline == null)
        {
            return false;
        }

        if (slot.PropertyId == AnimationTimelineSelectionComponentProperties.DurationFramesId)
        {
            int duration = Math.Max(1, newValue.Int);
            timeline.DurationFrames = duration;
            timeline.WorkStartFrame = Math.Clamp(timeline.WorkStartFrame, 0, duration);
            timeline.WorkEndFrame = Math.Clamp(timeline.WorkEndFrame, 0, duration);
            if (timeline.WorkEndFrame < timeline.WorkStartFrame)
            {
                timeline.WorkEndFrame = timeline.WorkStartFrame;
            }
            return true;
        }

        if (slot.PropertyId == AnimationTimelineSelectionComponentProperties.WorkStartFrameId)
        {
            int duration = Math.Max(1, timeline.DurationFrames);
            timeline.WorkStartFrame = Math.Clamp(newValue.Int, 0, duration);
            if (timeline.WorkEndFrame < timeline.WorkStartFrame)
            {
                timeline.WorkEndFrame = timeline.WorkStartFrame;
            }
            return true;
        }

        if (slot.PropertyId == AnimationTimelineSelectionComponentProperties.WorkEndFrameId)
        {
            int duration = Math.Max(1, timeline.DurationFrames);
            timeline.WorkEndFrame = Math.Clamp(newValue.Int, 0, duration);
            if (timeline.WorkEndFrame < timeline.WorkStartFrame)
            {
                timeline.WorkStartFrame = timeline.WorkEndFrame;
            }
            return true;
        }

        if (slot.PropertyId == AnimationTimelineSelectionComponentProperties.SnapFpsId)
        {
            timeline.SnapFps = Math.Clamp(newValue.Int, 1, 240);
            return true;
        }

        if (slot.PropertyId == AnimationTimelineSelectionComponentProperties.PlaybackSpeedId)
        {
            float speed = newValue.Float;
            if (!float.IsFinite(speed) || speed <= 0f)
            {
                speed = 1f;
            }
            timeline.PlaybackSpeed = speed;
            return true;
        }

        if (slot.PropertyId == AnimationTimelineSelectionComponentProperties.PlaybackModeId)
        {
            int mode = Math.Clamp(newValue.Int, 0, 2);
            timeline.Mode = (AnimationDocument.PlaybackMode)mode;
            return true;
        }

        return false;
    }

    private bool TryApplyKeyframeSelectionPropertyEdit(AnimationDocument doc, PropertySlot slot, in PropertyValue newValue)
    {
        if (doc == null)
        {
            return false;
        }

        ushort componentKind = slot.Component.Kind;

        // Read selection metadata from the component, then apply the change to the backing AnimationDocument.
        if (!TryReadKeySelection(_workspace.PropertyWorld, slot.Component, out int timelineId, out int keyIndex, out AnimationDocument.AnimationBinding binding))
        {
            return false;
        }

        if (!doc.TryGetTimelineById(timelineId, out var timeline) || timeline == null)
        {
            return false;
        }

        AnimationDocument.AnimationTrack? track = AnimationEditorHelpers.FindTrack(timeline, binding);
        if (track == null)
        {
            return false;
        }

        if ((uint)keyIndex >= (uint)track.Keys.Count)
        {
            return false;
        }

        var key = track.Keys[keyIndex];

        if (componentKind == AnimationKeyframeSelectionFloatComponent.Api.PoolIdConst && slot.PropertyId == AnimationKeyframeSelectionFloatComponentProperties.ValueId)
        {
            key.Value = PropertyValue.FromFloat(newValue.Float);
            track.Keys[keyIndex] = key;
            return true;
        }
        if (componentKind == AnimationKeyframeSelectionIntComponent.Api.PoolIdConst && slot.PropertyId == AnimationKeyframeSelectionIntComponentProperties.ValueId)
        {
            key.Value = PropertyValue.FromInt(newValue.Int);
            track.Keys[keyIndex] = key;
            return true;
        }
        if (componentKind == AnimationKeyframeSelectionBoolComponent.Api.PoolIdConst && slot.PropertyId == AnimationKeyframeSelectionBoolComponentProperties.ValueId)
        {
            key.Value = PropertyValue.FromBool(newValue.Bool);
            track.Keys[keyIndex] = key;
            return true;
        }
        if (componentKind == AnimationKeyframeSelectionVec2Component.Api.PoolIdConst && slot.PropertyId == AnimationKeyframeSelectionVec2ComponentProperties.ValueId)
        {
            key.Value = PropertyValue.FromVec2(newValue.Vec2);
            track.Keys[keyIndex] = key;
            return true;
        }
        if (componentKind == AnimationKeyframeSelectionVec3Component.Api.PoolIdConst && slot.PropertyId == AnimationKeyframeSelectionVec3ComponentProperties.ValueId)
        {
            key.Value = PropertyValue.FromVec3(newValue.Vec3);
            track.Keys[keyIndex] = key;
            return true;
        }
        if (componentKind == AnimationKeyframeSelectionVec4Component.Api.PoolIdConst && slot.PropertyId == AnimationKeyframeSelectionVec4ComponentProperties.ValueId)
        {
            key.Value = PropertyValue.FromVec4(newValue.Vec4);
            track.Keys[keyIndex] = key;
            return true;
        }
        if (componentKind == AnimationKeyframeSelectionColor32Component.Api.PoolIdConst && slot.PropertyId == AnimationKeyframeSelectionColor32ComponentProperties.ValueId)
        {
            key.Value = PropertyValue.FromColor32(newValue.Color32);
            track.Keys[keyIndex] = key;
            return true;
        }

        ulong interpolationId =
            componentKind == AnimationKeyframeSelectionFloatComponent.Api.PoolIdConst ? AnimationKeyframeSelectionFloatComponentProperties.InterpolationId :
            componentKind == AnimationKeyframeSelectionIntComponent.Api.PoolIdConst ? AnimationKeyframeSelectionIntComponentProperties.InterpolationId :
            componentKind == AnimationKeyframeSelectionBoolComponent.Api.PoolIdConst ? AnimationKeyframeSelectionBoolComponentProperties.InterpolationId :
            componentKind == AnimationKeyframeSelectionVec2Component.Api.PoolIdConst ? AnimationKeyframeSelectionVec2ComponentProperties.InterpolationId :
            componentKind == AnimationKeyframeSelectionVec3Component.Api.PoolIdConst ? AnimationKeyframeSelectionVec3ComponentProperties.InterpolationId :
            componentKind == AnimationKeyframeSelectionVec4Component.Api.PoolIdConst ? AnimationKeyframeSelectionVec4ComponentProperties.InterpolationId :
            componentKind == AnimationKeyframeSelectionColor32Component.Api.PoolIdConst ? AnimationKeyframeSelectionColor32ComponentProperties.InterpolationId :
            0;

        if (interpolationId != 0 && slot.PropertyId == interpolationId)
        {
            int interp = Math.Clamp(newValue.Int, 0, 2);
            key.Interpolation = (AnimationDocument.Interpolation)interp;
            track.Keys[keyIndex] = key;
            return true;
        }

        ulong inTangentId =
            componentKind == AnimationKeyframeSelectionFloatComponent.Api.PoolIdConst ? AnimationKeyframeSelectionFloatComponentProperties.InTangentId :
            componentKind == AnimationKeyframeSelectionIntComponent.Api.PoolIdConst ? AnimationKeyframeSelectionIntComponentProperties.InTangentId :
            componentKind == AnimationKeyframeSelectionBoolComponent.Api.PoolIdConst ? AnimationKeyframeSelectionBoolComponentProperties.InTangentId :
            componentKind == AnimationKeyframeSelectionVec2Component.Api.PoolIdConst ? AnimationKeyframeSelectionVec2ComponentProperties.InTangentId :
            componentKind == AnimationKeyframeSelectionVec3Component.Api.PoolIdConst ? AnimationKeyframeSelectionVec3ComponentProperties.InTangentId :
            componentKind == AnimationKeyframeSelectionVec4Component.Api.PoolIdConst ? AnimationKeyframeSelectionVec4ComponentProperties.InTangentId :
            componentKind == AnimationKeyframeSelectionColor32Component.Api.PoolIdConst ? AnimationKeyframeSelectionColor32ComponentProperties.InTangentId :
            0;

        if (inTangentId != 0 && slot.PropertyId == inTangentId)
        {
            key.InTangent = newValue.Float;
            track.Keys[keyIndex] = key;
            return true;
        }

        ulong outTangentId =
            componentKind == AnimationKeyframeSelectionFloatComponent.Api.PoolIdConst ? AnimationKeyframeSelectionFloatComponentProperties.OutTangentId :
            componentKind == AnimationKeyframeSelectionIntComponent.Api.PoolIdConst ? AnimationKeyframeSelectionIntComponentProperties.OutTangentId :
            componentKind == AnimationKeyframeSelectionBoolComponent.Api.PoolIdConst ? AnimationKeyframeSelectionBoolComponentProperties.OutTangentId :
            componentKind == AnimationKeyframeSelectionVec2Component.Api.PoolIdConst ? AnimationKeyframeSelectionVec2ComponentProperties.OutTangentId :
            componentKind == AnimationKeyframeSelectionVec3Component.Api.PoolIdConst ? AnimationKeyframeSelectionVec3ComponentProperties.OutTangentId :
            componentKind == AnimationKeyframeSelectionVec4Component.Api.PoolIdConst ? AnimationKeyframeSelectionVec4ComponentProperties.OutTangentId :
            componentKind == AnimationKeyframeSelectionColor32Component.Api.PoolIdConst ? AnimationKeyframeSelectionColor32ComponentProperties.OutTangentId :
            0;

        if (outTangentId != 0 && slot.PropertyId == outTangentId)
        {
            key.OutTangent = newValue.Float;
            track.Keys[keyIndex] = key;
            return true;
        }

        return false;
    }

    private static bool TryReadKeySelection(
        Pooled.Runtime.IPoolRegistry propertyWorld,
        in AnyComponentHandle selection,
        out int timelineId,
        out int keyIndex,
        out AnimationDocument.AnimationBinding binding)
    {
        timelineId = 0;
        keyIndex = 0;
        binding = default;

        ushort kind = selection.Kind;

        if (kind == AnimationKeyframeSelectionFloatComponent.Api.PoolIdConst)
        {
            var handle = new AnimationKeyframeSelectionFloatComponentHandle(selection.Index, selection.Generation);
            var view = AnimationKeyframeSelectionFloatComponent.Api.FromHandle(propertyWorld, handle);
            if (!view.IsAlive)
            {
                return false;
            }
            timelineId = view.TimelineId;
            keyIndex = view.KeyIndex;
            binding = new AnimationDocument.AnimationBinding(view.TargetIndex, view.ComponentKind, view.PropertyIndexHint, view.PropertyId, view.BoundPropertyKind);
            return true;
        }
        if (kind == AnimationKeyframeSelectionIntComponent.Api.PoolIdConst)
        {
            var handle = new AnimationKeyframeSelectionIntComponentHandle(selection.Index, selection.Generation);
            var view = AnimationKeyframeSelectionIntComponent.Api.FromHandle(propertyWorld, handle);
            if (!view.IsAlive)
            {
                return false;
            }
            timelineId = view.TimelineId;
            keyIndex = view.KeyIndex;
            binding = new AnimationDocument.AnimationBinding(view.TargetIndex, view.ComponentKind, view.PropertyIndexHint, view.PropertyId, view.BoundPropertyKind);
            return true;
        }
        if (kind == AnimationKeyframeSelectionBoolComponent.Api.PoolIdConst)
        {
            var handle = new AnimationKeyframeSelectionBoolComponentHandle(selection.Index, selection.Generation);
            var view = AnimationKeyframeSelectionBoolComponent.Api.FromHandle(propertyWorld, handle);
            if (!view.IsAlive)
            {
                return false;
            }
            timelineId = view.TimelineId;
            keyIndex = view.KeyIndex;
            binding = new AnimationDocument.AnimationBinding(view.TargetIndex, view.ComponentKind, view.PropertyIndexHint, view.PropertyId, view.BoundPropertyKind);
            return true;
        }
        if (kind == AnimationKeyframeSelectionVec2Component.Api.PoolIdConst)
        {
            var handle = new AnimationKeyframeSelectionVec2ComponentHandle(selection.Index, selection.Generation);
            var view = AnimationKeyframeSelectionVec2Component.Api.FromHandle(propertyWorld, handle);
            if (!view.IsAlive)
            {
                return false;
            }
            timelineId = view.TimelineId;
            keyIndex = view.KeyIndex;
            binding = new AnimationDocument.AnimationBinding(view.TargetIndex, view.ComponentKind, view.PropertyIndexHint, view.PropertyId, view.BoundPropertyKind);
            return true;
        }
        if (kind == AnimationKeyframeSelectionVec3Component.Api.PoolIdConst)
        {
            var handle = new AnimationKeyframeSelectionVec3ComponentHandle(selection.Index, selection.Generation);
            var view = AnimationKeyframeSelectionVec3Component.Api.FromHandle(propertyWorld, handle);
            if (!view.IsAlive)
            {
                return false;
            }
            timelineId = view.TimelineId;
            keyIndex = view.KeyIndex;
            binding = new AnimationDocument.AnimationBinding(view.TargetIndex, view.ComponentKind, view.PropertyIndexHint, view.PropertyId, view.BoundPropertyKind);
            return true;
        }
        if (kind == AnimationKeyframeSelectionVec4Component.Api.PoolIdConst)
        {
            var handle = new AnimationKeyframeSelectionVec4ComponentHandle(selection.Index, selection.Generation);
            var view = AnimationKeyframeSelectionVec4Component.Api.FromHandle(propertyWorld, handle);
            if (!view.IsAlive)
            {
                return false;
            }
            timelineId = view.TimelineId;
            keyIndex = view.KeyIndex;
            binding = new AnimationDocument.AnimationBinding(view.TargetIndex, view.ComponentKind, view.PropertyIndexHint, view.PropertyId, view.BoundPropertyKind);
            return true;
        }
        if (kind == AnimationKeyframeSelectionColor32Component.Api.PoolIdConst)
        {
            var handle = new AnimationKeyframeSelectionColor32ComponentHandle(selection.Index, selection.Generation);
            var view = AnimationKeyframeSelectionColor32Component.Api.FromHandle(propertyWorld, handle);
            if (!view.IsAlive)
            {
                return false;
            }
            timelineId = view.TimelineId;
            keyIndex = view.KeyIndex;
            binding = new AnimationDocument.AnimationBinding(view.TargetIndex, view.ComponentKind, view.PropertyIndexHint, view.PropertyId, view.BoundPropertyKind);
            return true;
        }

        return false;
    }

    internal void NotifyCanvasPropertyEdited(EntityId entity, PropertySlot slot, in PropertyValue oldValue, in PropertyValue newValue)
    {
        if (!_workspace.TryGetActiveAnimationDocument(out var animations) || !animations.TryGetSelectedTimeline(out var timeline) || timeline == null)
        {
            return;
        }

        if (entity.IsNull)
        {
            return;
        }

        uint stableId = _workspace.World.GetStableId(entity);
        if (stableId == 0)
        {
            return;
        }

        OnPropertyEditedWhileTimelineSelected(stableId, slot, oldValue, newValue, timeline);
    }

    private void OnPropertyEditedWhileTimelineSelected(uint targetStableId, PropertySlot slot, in PropertyValue oldValue, in PropertyValue newValue, AnimationDocument.AnimationTimeline timeline)
    {
        if (targetStableId == 0)
        {
            return;
        }

        bool canCreateKeys = AnimationEditorWindow.IsAutoKeyEnabled();
        int frame = Math.Clamp(AnimationEditorWindow.GetCurrentFrame(), 0, Math.Max(1, timeline.DurationFrames));

        bool hasTarget = AnimationEditorHelpers.TryGetTargetIndex(timeline, targetStableId, out int targetIndex);
        if (!hasTarget)
        {
            if (!canCreateKeys)
            {
                return;
            }
            targetIndex = AnimationEditorHelpers.GetOrAddTargetIndex(timeline, targetStableId);
            if (targetIndex < 0)
            {
                return;
            }
        }

        if (PropertyDispatcher.TryGetInfo(slot.Component, (int)slot.PropertyIndex, out var info) && info.HasChannels && !info.IsChannel)
        {
            byte changedMask = 0xFF;
            if (slot.Kind == PropertyKind.Vec2)
            {
                changedMask = 0;
                if (MathF.Abs(oldValue.Vec2.X - newValue.Vec2.X) > 0.0001f)
                {
                    changedMask |= 1 << 0;
                }
                if (MathF.Abs(oldValue.Vec2.Y - newValue.Vec2.Y) > 0.0001f)
                {
                    changedMask |= 1 << 1;
                }
                if (changedMask == 0)
                {
                    return;
                }
            }
            else if (slot.Kind == PropertyKind.Vec3)
            {
                changedMask = 0;
                if (MathF.Abs(oldValue.Vec3.X - newValue.Vec3.X) > 0.0001f)
                {
                    changedMask |= 1 << 0;
                }
                if (MathF.Abs(oldValue.Vec3.Y - newValue.Vec3.Y) > 0.0001f)
                {
                    changedMask |= 1 << 1;
                }
                if (MathF.Abs(oldValue.Vec3.Z - newValue.Vec3.Z) > 0.0001f)
                {
                    changedMask |= 1 << 2;
                }
                if (changedMask == 0)
                {
                    return;
                }
            }
            else if (slot.Kind == PropertyKind.Vec4)
            {
                changedMask = 0;
                if (MathF.Abs(oldValue.Vec4.X - newValue.Vec4.X) > 0.0001f)
                {
                    changedMask |= 1 << 0;
                }
                if (MathF.Abs(oldValue.Vec4.Y - newValue.Vec4.Y) > 0.0001f)
                {
                    changedMask |= 1 << 1;
                }
                if (MathF.Abs(oldValue.Vec4.Z - newValue.Vec4.Z) > 0.0001f)
                {
                    changedMask |= 1 << 2;
                }
                if (MathF.Abs(oldValue.Vec4.W - newValue.Vec4.W) > 0.0001f)
                {
                    changedMask |= 1 << 3;
                }
                if (changedMask == 0)
                {
                    return;
                }
            }

            int propertyCount = PropertyDispatcher.GetPropertyCount(slot.Component);
            if (propertyCount <= 0)
            {
                return;
            }

            for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
            {
                if (!PropertyDispatcher.TryGetInfo(slot.Component, propertyIndex, out var channelInfo))
                {
                    continue;
                }

                if (!channelInfo.IsChannel || channelInfo.ChannelGroupId != info.ChannelGroupId)
                {
                    continue;
                }

                if (changedMask != 0xFF)
                {
                    int channelIndex = channelInfo.ChannelIndex;
                    if ((uint)channelIndex >= 8u)
                    {
                        continue;
                    }
                    if ((changedMask & (1 << channelIndex)) == 0)
                    {
                        continue;
                    }
                }

                var channelBinding = new AnimationDocument.AnimationBinding(
                    targetIndex: targetIndex,
                    componentKind: slot.Component.Kind,
                    propertyIndexHint: (ushort)propertyIndex,
                    propertyId: channelInfo.PropertyId,
                    propertyKind: channelInfo.Kind);

                var track = AnimationEditorHelpers.FindTrack(timeline, channelBinding);
                if (track != null)
                {
                    PropertySlot channelSlot = PropertyDispatcher.GetSlot(slot.Component, propertyIndex);
                    PropertyValue channelValue = ReadValue(_workspace.PropertyWorld, channelSlot);
                    if (TrySetKeyValueAtFrame(track, frame, channelValue))
                    {
                        continue;
                    }
                }

                if (canCreateKeys)
                {
                    AnimationEditorWindow.AutoKeyAtCurrentFrame(_workspace, timeline, channelBinding, selectKeyframe: false);
                }
            }

            return;
        }

        var binding = new AnimationDocument.AnimationBinding(
            targetIndex: targetIndex,
            componentKind: slot.Component.Kind,
            propertyIndexHint: slot.PropertyIndex,
            propertyId: slot.PropertyId,
            propertyKind: slot.Kind);

        var directTrack = AnimationEditorHelpers.FindTrack(timeline, binding);
        if (directTrack != null && TrySetKeyValueAtFrame(directTrack, frame, newValue))
        {
            return;
        }

        if (canCreateKeys)
        {
            AnimationEditorWindow.AutoKeyAtCurrentFrame(_workspace, timeline, binding, selectKeyframe: false);
        }
    }

    private static bool TrySetKeyValueAtFrame(AnimationDocument.AnimationTrack track, int frame, in PropertyValue newValue)
    {
        for (int i = 0; i < track.Keys.Count; i++)
        {
            if (track.Keys[i].Frame != frame)
            {
                continue;
            }

            var k = track.Keys[i];
            k.Value = newValue;
            track.Keys[i] = k;
            return true;
        }

        return false;
    }

    private static bool TryRemoveKeyAtFrame(AnimationDocument.AnimationTrack track, int frame)
    {
        for (int i = 0; i < track.Keys.Count; i++)
        {
            if (track.Keys[i].Frame != frame)
            {
                continue;
            }

            track.Keys.RemoveAt(i);
            return true;
        }

        return false;
    }

    private static void RemoveTrack(AnimationDocument.AnimationTimeline timeline, AnimationDocument.AnimationTrack track)
    {
        for (int i = 0; i < timeline.Tracks.Count; i++)
        {
            if (ReferenceEquals(timeline.Tracks[i], track))
            {
                timeline.Tracks.RemoveAt(i);
                return;
            }
        }
    }

    public bool TryGetKeyableState(PropertySlot slot, out bool hasTrack, out bool hasKeyAtPlayhead)
    {
        hasTrack = false;
        hasKeyAtPlayhead = false;

        if (!_workspace.TryGetActiveAnimationDocument(out var animations) || !animations.TryGetSelectedTimeline(out var timeline) || timeline == null)
        {
            return false;
        }

        if (!AnimationEditorHelpers.TryGetSelectionTargetStableId(_workspace, out uint targetStableId))
        {
            return false;
        }

        if (PropertyDispatcher.TryGetInfo(slot.Component, (int)slot.PropertyIndex, out var info) && info.HasChannels && !info.IsChannel)
        {
            return false;
        }

        int frame = Math.Clamp(AnimationEditorWindow.GetCurrentFrame(), 0, Math.Max(1, timeline.DurationFrames));

        if (!AnimationEditorHelpers.TryGetTargetIndex(timeline, targetStableId, out int targetIndex))
        {
            return true;
        }

        var binding = new AnimationDocument.AnimationBinding(
            targetIndex: targetIndex,
            componentKind: slot.Component.Kind,
            propertyIndexHint: slot.PropertyIndex,
            propertyId: slot.PropertyId,
            propertyKind: slot.Kind);

        var track = AnimationEditorHelpers.FindTrack(timeline, binding);
        hasTrack = track != null;
        hasKeyAtPlayhead = track != null && AnimationEditorHelpers.HasKeyAtFrame(timeline, binding, frame);
        return true;
    }

    public bool TryGetKeyableState(PropertySlot slot, out bool hasKeyAtPlayhead)
    {
        return TryGetKeyableState(slot, out _, out hasKeyAtPlayhead);
    }

    public void AddKeyAtPlayhead(PropertySlot slot)
    {
        if (!_workspace.TryGetActiveAnimationDocument(out var animations) || !animations.TryGetSelectedTimeline(out var timeline) || timeline == null)
        {
            return;
        }

        if (!AnimationEditorHelpers.TryGetSelectionTargetStableId(_workspace, out uint targetStableId))
        {
            return;
        }

        if (PropertyDispatcher.TryGetInfo(slot.Component, (int)slot.PropertyIndex, out var info) && info.HasChannels && !info.IsChannel)
        {
            return;
        }

        int targetIndex = AnimationEditorHelpers.GetOrAddTargetIndex(timeline, targetStableId);
        if (targetIndex < 0)
        {
            return;
        }
        var binding = new AnimationDocument.AnimationBinding(
            targetIndex: targetIndex,
            componentKind: slot.Component.Kind,
            propertyIndexHint: slot.PropertyIndex,
            propertyId: slot.PropertyId,
            propertyKind: slot.Kind);

        AnimationEditorWindow.AutoKeyAtCurrentFrame(_workspace, timeline, binding, selectKeyframe: false);
        AnimationEditorWindow.MarkPreviewDirty();
    }

    public void ToggleKeyAtPlayhead(PropertySlot slot)
    {
        if (!TryGetKeyableState(slot, out _, out bool hasKeyAtPlayhead))
        {
            return;
        }

        if (hasKeyAtPlayhead)
        {
            RemoveKeyAtPlayhead(slot);
            return;
        }

        AddKeyAtPlayhead(slot);
    }

    public bool TryGetKeyableChannelState(PropertySlot rootSlot, ushort channelIndex, out bool hasTrack, out bool hasKeyAtPlayhead)
    {
        hasTrack = false;
        hasKeyAtPlayhead = false;

        if (!_workspace.TryGetActiveAnimationDocument(out var animations) || !animations.TryGetSelectedTimeline(out var timeline) || timeline == null)
        {
            return false;
        }

        if (!AnimationEditorHelpers.TryGetSelectionTargetStableId(_workspace, out uint targetStableId))
        {
            return false;
        }

        if (!PropertyDispatcher.TryGetInfo(rootSlot.Component, (int)rootSlot.PropertyIndex, out var rootInfo))
        {
            return false;
        }

        if (!rootInfo.HasChannels || rootInfo.IsChannel)
        {
            return false;
        }

        int propertyCount = PropertyDispatcher.GetPropertyCount(rootSlot.Component);
        if (propertyCount <= 0)
        {
            return false;
        }

        ushort channelPropertyIndexHint = 0;
        ulong channelPropertyId = 0;
        PropertyKind channelKind = PropertyKind.Auto;
        bool found = false;

        for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
        {
            if (!PropertyDispatcher.TryGetInfo(rootSlot.Component, propertyIndex, out var channelInfo))
            {
                continue;
            }

            if (!channelInfo.IsChannel || channelInfo.ChannelGroupId != rootInfo.ChannelGroupId || channelInfo.ChannelIndex != channelIndex)
            {
                continue;
            }

            channelPropertyIndexHint = (ushort)propertyIndex;
            channelPropertyId = channelInfo.PropertyId;
            channelKind = channelInfo.Kind;
            found = true;
            break;
        }

        if (!found)
        {
            return false;
        }

        int frame = Math.Clamp(AnimationEditorWindow.GetCurrentFrame(), 0, Math.Max(1, timeline.DurationFrames));
        if (!AnimationEditorHelpers.TryGetTargetIndex(timeline, targetStableId, out int targetIndex))
        {
            return true;
        }

        var binding = new AnimationDocument.AnimationBinding(
            targetIndex: targetIndex,
            componentKind: rootSlot.Component.Kind,
            propertyIndexHint: channelPropertyIndexHint,
            propertyId: channelPropertyId,
            propertyKind: channelKind);

        var track = AnimationEditorHelpers.FindTrack(timeline, binding);
        hasTrack = track != null;
        hasKeyAtPlayhead = track != null && AnimationEditorHelpers.HasKeyAtFrame(timeline, binding, frame);
        return true;
    }

    public void AddKeyAtPlayhead(PropertySlot rootSlot, ushort channelIndex)
    {
        if (!_workspace.TryGetActiveAnimationDocument(out var animations) || !animations.TryGetSelectedTimeline(out var timeline) || timeline == null)
        {
            return;
        }

        if (!AnimationEditorHelpers.TryGetSelectionTargetStableId(_workspace, out uint targetStableId))
        {
            return;
        }

        if (!PropertyDispatcher.TryGetInfo(rootSlot.Component, (int)rootSlot.PropertyIndex, out var rootInfo))
        {
            return;
        }

        if (!rootInfo.HasChannels || rootInfo.IsChannel)
        {
            return;
        }

        int propertyCount = PropertyDispatcher.GetPropertyCount(rootSlot.Component);
        if (propertyCount <= 0)
        {
            return;
        }

        ushort channelPropertyIndexHint = 0;
        ulong channelPropertyId = 0;
        PropertyKind channelKind = PropertyKind.Auto;
        bool found = false;

        for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
        {
            if (!PropertyDispatcher.TryGetInfo(rootSlot.Component, propertyIndex, out var channelInfo))
            {
                continue;
            }

            if (!channelInfo.IsChannel || channelInfo.ChannelGroupId != rootInfo.ChannelGroupId || channelInfo.ChannelIndex != channelIndex)
            {
                continue;
            }

            channelPropertyIndexHint = (ushort)propertyIndex;
            channelPropertyId = channelInfo.PropertyId;
            channelKind = channelInfo.Kind;
            found = true;
            break;
        }

        if (!found)
        {
            return;
        }

        int targetIndex = AnimationEditorHelpers.GetOrAddTargetIndex(timeline, targetStableId);
        if (targetIndex < 0)
        {
            return;
        }
        var binding = new AnimationDocument.AnimationBinding(
            targetIndex: targetIndex,
            componentKind: rootSlot.Component.Kind,
            propertyIndexHint: channelPropertyIndexHint,
            propertyId: channelPropertyId,
            propertyKind: channelKind);

        AnimationEditorWindow.AutoKeyAtCurrentFrame(_workspace, timeline, binding, selectKeyframe: false);
        AnimationEditorWindow.MarkPreviewDirty();
    }

    public void ToggleKeyAtPlayhead(PropertySlot rootSlot, ushort channelIndex)
    {
        if (!TryGetKeyableChannelState(rootSlot, channelIndex, out _, out bool hasKeyAtPlayhead))
        {
            return;
        }

        if (hasKeyAtPlayhead)
        {
            RemoveKeyAtPlayhead(rootSlot, channelIndex);
            return;
        }

        AddKeyAtPlayhead(rootSlot, channelIndex);
    }

    private void RemoveKeyAtPlayhead(PropertySlot slot)
    {
        if (!_workspace.TryGetActiveAnimationDocument(out var animations) || !animations.TryGetSelectedTimeline(out var timeline) || timeline == null)
        {
            return;
        }

        if (!AnimationEditorHelpers.TryGetSelectionTargetStableId(_workspace, out uint targetStableId))
        {
            return;
        }

        if (PropertyDispatcher.TryGetInfo(slot.Component, (int)slot.PropertyIndex, out var info) && info.HasChannels && !info.IsChannel)
        {
            return;
        }

        int durationFrames = Math.Max(1, timeline.DurationFrames);
        int frame = Math.Clamp(AnimationEditorWindow.GetCurrentFrame(), 0, durationFrames);

        if (!AnimationEditorHelpers.TryGetTargetIndex(timeline, targetStableId, out int targetIndex))
        {
            return;
        }

        var binding = new AnimationDocument.AnimationBinding(
            targetIndex: targetIndex,
            componentKind: slot.Component.Kind,
            propertyIndexHint: slot.PropertyIndex,
            propertyId: slot.PropertyId,
            propertyKind: slot.Kind);

        AnimationDocument.AnimationTrack? track = AnimationEditorHelpers.FindTrack(timeline, binding);
        if (track == null)
        {
            return;
        }

        if (!TryRemoveKeyAtFrame(track, frame))
        {
            return;
        }

        if (track.Keys.Count <= 0)
        {
            RemoveTrack(timeline, track);
        }

        AnimationEditorWindow.MarkPreviewDirty();
    }

    private void RemoveKeyAtPlayhead(PropertySlot rootSlot, ushort channelIndex)
    {
        if (!_workspace.TryGetActiveAnimationDocument(out var animations) || !animations.TryGetSelectedTimeline(out var timeline) || timeline == null)
        {
            return;
        }

        if (!AnimationEditorHelpers.TryGetSelectionTargetStableId(_workspace, out uint targetStableId))
        {
            return;
        }

        if (!PropertyDispatcher.TryGetInfo(rootSlot.Component, (int)rootSlot.PropertyIndex, out var rootInfo))
        {
            return;
        }

        if (!rootInfo.HasChannels || rootInfo.IsChannel)
        {
            return;
        }

        int propertyCount = PropertyDispatcher.GetPropertyCount(rootSlot.Component);
        if (propertyCount <= 0)
        {
            return;
        }

        ushort channelPropertyIndexHint = 0;
        ulong channelPropertyId = 0;
        PropertyKind channelKind = PropertyKind.Auto;
        bool found = false;

        for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
        {
            if (!PropertyDispatcher.TryGetInfo(rootSlot.Component, propertyIndex, out var channelInfo))
            {
                continue;
            }

            if (!channelInfo.IsChannel || channelInfo.ChannelGroupId != rootInfo.ChannelGroupId || channelInfo.ChannelIndex != channelIndex)
            {
                continue;
            }

            channelPropertyIndexHint = (ushort)propertyIndex;
            channelPropertyId = channelInfo.PropertyId;
            channelKind = channelInfo.Kind;
            found = true;
            break;
        }

        if (!found)
        {
            return;
        }

        int durationFrames = Math.Max(1, timeline.DurationFrames);
        int frame = Math.Clamp(AnimationEditorWindow.GetCurrentFrame(), 0, durationFrames);

        if (!AnimationEditorHelpers.TryGetTargetIndex(timeline, targetStableId, out int targetIndex))
        {
            return;
        }

        var binding = new AnimationDocument.AnimationBinding(
            targetIndex: targetIndex,
            componentKind: rootSlot.Component.Kind,
            propertyIndexHint: channelPropertyIndexHint,
            propertyId: channelPropertyId,
            propertyKind: channelKind);

        AnimationDocument.AnimationTrack? track = AnimationEditorHelpers.FindTrack(timeline, binding);
        if (track == null)
        {
            return;
        }

        if (!TryRemoveKeyAtFrame(track, frame))
        {
            return;
        }

        if (track.Keys.Count <= 0)
        {
            RemoveTrack(timeline, track);
        }

        AnimationEditorWindow.MarkPreviewDirty();
    }

    public void RecordFillComponentEdit(int widgetId, bool isEditing, FillComponentHandle fillHandle, in FillComponent before, in FillComponent after)
    {
        if (fillHandle.IsNull || before.Equals(after))
        {
            NotifyPropertyWidgetState(widgetId, isEditing);
            return;
        }

        AutoKeyFillComponentEdit(fillHandle, before, after);

        if (_pendingEdit.IsActive && _pendingEdit.WidgetId == widgetId && _pendingEdit.PendingRecord.Kind == UndoRecordKind.SetFillComponent &&
            _pendingEdit.PendingRecord.FillHandle.Equals(fillHandle))
        {
            _pendingEdit.PendingRecord.FillAfter = after;
        }
        else
        {
            if (_pendingEdit.IsActive)
            {
                CommitPendingEdit();
            }

            _pendingEdit = new PendingEdit
            {
                WidgetId = widgetId,
                PendingRecord = UndoRecord.ForFill(fillHandle, before, after),
                IsActive = true
            };
        }

        if (!isEditing)
        {
            CommitPendingEdit();
            return;
        }

        NotifyPropertyWidgetState(widgetId, isEditing);
    }

    private struct FillAutoKeySlots
    {
        public PropertySlot Color;
        public PropertySlot UseGradient;
        public PropertySlot GradientColorA;
        public PropertySlot GradientColorB;
        public PropertySlot GradientMix;
        public PropertySlot GradientDirection;
        public PropertySlot StopsT0To3;
        public PropertySlot StopsT4To7;
        public PropertySlot StopsColor0;
        public PropertySlot StopsColor1;
        public PropertySlot StopsColor2;
        public PropertySlot StopsColor3;
        public PropertySlot StopsColor4;
        public PropertySlot StopsColor5;
        public PropertySlot StopsColor6;
        public PropertySlot StopsColor7;
    }

    private void AutoKeyFillComponentEdit(FillComponentHandle fillHandle, in FillComponent before, in FillComponent after)
    {
        if (!_workspace.TryGetActiveAnimationDocument(out var animations) || !animations.TryGetSelectedTimeline(out var timeline) || timeline == null)
        {
            return;
        }

        AnyComponentHandle component = FillComponentProperties.ToAnyHandle(fillHandle);
        int propertyCount = PropertyDispatcher.GetPropertyCount(component);
        if (propertyCount <= 0)
        {
            return;
        }

        if (!TryResolveFillAutoKeySlots(component, propertyCount, out FillAutoKeySlots slots))
        {
            return;
        }

        if (!before.Color.Equals(after.Color) && !slots.Color.Component.IsNull)
        {
            AutoKeyIfTimelineSelected(slots.Color, PropertyValue.FromColor32(before.Color), PropertyValue.FromColor32(after.Color));
        }

        if (before.UseGradient != after.UseGradient && !slots.UseGradient.Component.IsNull)
        {
            AutoKeyIfTimelineSelected(slots.UseGradient, PropertyValue.FromBool(before.UseGradient), PropertyValue.FromBool(after.UseGradient));
        }

        if (!before.GradientColorA.Equals(after.GradientColorA) && !slots.GradientColorA.Component.IsNull)
        {
            AutoKeyIfTimelineSelected(slots.GradientColorA, PropertyValue.FromColor32(before.GradientColorA), PropertyValue.FromColor32(after.GradientColorA));
        }

        if (!before.GradientColorB.Equals(after.GradientColorB) && !slots.GradientColorB.Component.IsNull)
        {
            AutoKeyIfTimelineSelected(slots.GradientColorB, PropertyValue.FromColor32(before.GradientColorB), PropertyValue.FromColor32(after.GradientColorB));
        }

        if (MathF.Abs(before.GradientMix - after.GradientMix) > 0.0001f && !slots.GradientMix.Component.IsNull)
        {
            AutoKeyIfTimelineSelected(slots.GradientMix, PropertyValue.FromFloat(before.GradientMix), PropertyValue.FromFloat(after.GradientMix));
        }

        if (before.GradientDirection != after.GradientDirection && !slots.GradientDirection.Component.IsNull)
        {
            AutoKeyIfTimelineSelected(slots.GradientDirection, PropertyValue.FromVec2(before.GradientDirection), PropertyValue.FromVec2(after.GradientDirection));
        }

        if (before.GradientStopT0To3 != after.GradientStopT0To3 && !slots.StopsT0To3.Component.IsNull)
        {
            AutoKeyIfTimelineSelected(slots.StopsT0To3, PropertyValue.FromVec4(before.GradientStopT0To3), PropertyValue.FromVec4(after.GradientStopT0To3));
        }

        if (before.GradientStopT4To7 != after.GradientStopT4To7 && !slots.StopsT4To7.Component.IsNull)
        {
            AutoKeyIfTimelineSelected(slots.StopsT4To7, PropertyValue.FromVec4(before.GradientStopT4To7), PropertyValue.FromVec4(after.GradientStopT4To7));
        }

        if (!before.GradientStopColor0.Equals(after.GradientStopColor0) && !slots.StopsColor0.Component.IsNull)
        {
            AutoKeyIfTimelineSelected(slots.StopsColor0, PropertyValue.FromColor32(before.GradientStopColor0), PropertyValue.FromColor32(after.GradientStopColor0));
        }

        if (!before.GradientStopColor1.Equals(after.GradientStopColor1) && !slots.StopsColor1.Component.IsNull)
        {
            AutoKeyIfTimelineSelected(slots.StopsColor1, PropertyValue.FromColor32(before.GradientStopColor1), PropertyValue.FromColor32(after.GradientStopColor1));
        }

        if (!before.GradientStopColor2.Equals(after.GradientStopColor2) && !slots.StopsColor2.Component.IsNull)
        {
            AutoKeyIfTimelineSelected(slots.StopsColor2, PropertyValue.FromColor32(before.GradientStopColor2), PropertyValue.FromColor32(after.GradientStopColor2));
        }

        if (!before.GradientStopColor3.Equals(after.GradientStopColor3) && !slots.StopsColor3.Component.IsNull)
        {
            AutoKeyIfTimelineSelected(slots.StopsColor3, PropertyValue.FromColor32(before.GradientStopColor3), PropertyValue.FromColor32(after.GradientStopColor3));
        }

        if (!before.GradientStopColor4.Equals(after.GradientStopColor4) && !slots.StopsColor4.Component.IsNull)
        {
            AutoKeyIfTimelineSelected(slots.StopsColor4, PropertyValue.FromColor32(before.GradientStopColor4), PropertyValue.FromColor32(after.GradientStopColor4));
        }

        if (!before.GradientStopColor5.Equals(after.GradientStopColor5) && !slots.StopsColor5.Component.IsNull)
        {
            AutoKeyIfTimelineSelected(slots.StopsColor5, PropertyValue.FromColor32(before.GradientStopColor5), PropertyValue.FromColor32(after.GradientStopColor5));
        }

        if (!before.GradientStopColor6.Equals(after.GradientStopColor6) && !slots.StopsColor6.Component.IsNull)
        {
            AutoKeyIfTimelineSelected(slots.StopsColor6, PropertyValue.FromColor32(before.GradientStopColor6), PropertyValue.FromColor32(after.GradientStopColor6));
        }

        if (!before.GradientStopColor7.Equals(after.GradientStopColor7) && !slots.StopsColor7.Component.IsNull)
        {
            AutoKeyIfTimelineSelected(slots.StopsColor7, PropertyValue.FromColor32(before.GradientStopColor7), PropertyValue.FromColor32(after.GradientStopColor7));
        }
    }

    private static bool TryResolveFillAutoKeySlots(AnyComponentHandle component, int propertyCount, out FillAutoKeySlots slots)
    {
        slots = default;

        for (int i = 0; i < propertyCount; i++)
        {
            if (!PropertyDispatcher.TryGetInfo(component, i, out PropertyInfo info))
            {
                continue;
            }

            if (info.IsChannel)
            {
                continue;
            }

            if (info.Group == FillGroupName)
            {
                if (info.Name == FillColorName)
                {
                    slots.Color = PropertyDispatcher.GetSlot(component, i);
                }
                else if (info.Name == FillUseGradientName)
                {
                    slots.UseGradient = PropertyDispatcher.GetSlot(component, i);
                }
                continue;
            }

            if (info.Group == GradientGroupName)
            {
                if (info.Name == GradientColorAName)
                {
                    slots.GradientColorA = PropertyDispatcher.GetSlot(component, i);
                }
                else if (info.Name == GradientColorBName)
                {
                    slots.GradientColorB = PropertyDispatcher.GetSlot(component, i);
                }
                else if (info.Name == GradientMixName)
                {
                    slots.GradientMix = PropertyDispatcher.GetSlot(component, i);
                }
                else if (info.Name == GradientDirectionName)
                {
                    slots.GradientDirection = PropertyDispatcher.GetSlot(component, i);
                }
                continue;
            }

            if (info.Group == StopsGroupName)
            {
                if (info.Name == StopsT0To3Name)
                {
                    slots.StopsT0To3 = PropertyDispatcher.GetSlot(component, i);
                }
                else if (info.Name == StopsT4To7Name)
                {
                    slots.StopsT4To7 = PropertyDispatcher.GetSlot(component, i);
                }
                else if (info.Name == StopsColor0Name)
                {
                    slots.StopsColor0 = PropertyDispatcher.GetSlot(component, i);
                }
                else if (info.Name == StopsColor1Name)
                {
                    slots.StopsColor1 = PropertyDispatcher.GetSlot(component, i);
                }
                else if (info.Name == StopsColor2Name)
                {
                    slots.StopsColor2 = PropertyDispatcher.GetSlot(component, i);
                }
                else if (info.Name == StopsColor3Name)
                {
                    slots.StopsColor3 = PropertyDispatcher.GetSlot(component, i);
                }
                else if (info.Name == StopsColor4Name)
                {
                    slots.StopsColor4 = PropertyDispatcher.GetSlot(component, i);
                }
                else if (info.Name == StopsColor5Name)
                {
                    slots.StopsColor5 = PropertyDispatcher.GetSlot(component, i);
                }
                else if (info.Name == StopsColor6Name)
                {
                    slots.StopsColor6 = PropertyDispatcher.GetSlot(component, i);
                }
                else if (info.Name == StopsColor7Name)
                {
                    slots.StopsColor7 = PropertyDispatcher.GetSlot(component, i);
                }
            }
        }

        return true;
    }

    private void CommitPendingEdit()
    {
        if (!_pendingEdit.IsActive)
        {
            return;
        }

        if (_pendingEdit.PendingRecord.Kind == UndoRecordKind.SetProperty)
        {
            if (_pendingEdit.PendingRecord.Before0.Equals(_pendingEdit.PendingRecord.Property0.Kind, _pendingEdit.PendingRecord.After0))
            {
                _pendingEdit = default;
                return;
            }
        }
        else if (_pendingEdit.PendingRecord.Kind == UndoRecordKind.SetPropertyBatch)
        {
            bool allSame = true;
            byte count = _pendingEdit.PendingRecord.PropertyCount;
            if (count >= 1)
            {
                allSame &= _pendingEdit.PendingRecord.Before0.Equals(_pendingEdit.PendingRecord.Property0.Kind, _pendingEdit.PendingRecord.After0);
            }
            if (count >= 2)
            {
                allSame &= _pendingEdit.PendingRecord.Before1.Equals(_pendingEdit.PendingRecord.Property1.Kind, _pendingEdit.PendingRecord.After1);
            }
            if (count >= 3)
            {
                allSame &= _pendingEdit.PendingRecord.Before2.Equals(_pendingEdit.PendingRecord.Property2.Kind, _pendingEdit.PendingRecord.After2);
            }
            if (count >= 4)
            {
                allSame &= _pendingEdit.PendingRecord.Before3.Equals(_pendingEdit.PendingRecord.Property3.Kind, _pendingEdit.PendingRecord.After3);
            }

            if (allSame)
            {
                _pendingEdit = default;
                return;
            }
        }
        else if (_pendingEdit.PendingRecord.Kind == UndoRecordKind.SetPropertyMany)
        {
            int start = _pendingEdit.PendingRecord.PayloadStart;
            int count = _pendingEdit.PendingRecord.PayloadCount;
            if (start < 0 || count <= 0 || start + count > _undoPropertyPayload.Count)
            {
                _pendingEdit = default;
                return;
            }

            bool allSame = true;
            ReadOnlySpan<PropertyUndoPayload> payload = CollectionsMarshal.AsSpan(_undoPropertyPayload);
            for (int i = 0; i < count; i++)
            {
                ref readonly PropertyUndoPayload entry = ref payload[start + i];
                if (entry.Key.Component.IsNull)
                {
                    continue;
                }

                allSame &= entry.Before.Equals(entry.Key.Kind, entry.After);
            }

            if (allSame)
            {
                if (start + count == _undoPropertyPayload.Count)
                {
                    _undoPropertyPayload.RemoveRange(start, count);
                }

                _pendingEdit = default;
                return;
            }
        }
        else if (_pendingEdit.PendingRecord.Kind == UndoRecordKind.SetComponentSnapshot)
        {
            int payloadIndex = _pendingEdit.PendingRecord.ComponentSnapshotIndex;
            if ((uint)payloadIndex >= (uint)_undoComponentSnapshotPayload.Count)
            {
                _pendingEdit = default;
                return;
            }

            ref ComponentSnapshotUndoPayload payload = ref CollectionsMarshal.AsSpan(_undoComponentSnapshotPayload)[payloadIndex];
            if (payload.Before.AsSpan().SequenceEqual(payload.After))
            {
                if (payloadIndex == _undoComponentSnapshotPayload.Count - 1)
                {
                    _undoComponentSnapshotPayload.RemoveAt(payloadIndex);
                }

                _pendingEdit = default;
                return;
            }
        }
        else if (_pendingEdit.PendingRecord.Kind == UndoRecordKind.SetComponentSnapshotMany)
        {
            int start = _pendingEdit.PendingRecord.PayloadStart;
            int count = _pendingEdit.PendingRecord.PayloadCount;
            if (start < 0 || count <= 0 || start + count > _undoComponentSnapshotPayload.Count)
            {
                _pendingEdit = default;
                return;
            }

            bool allSame = true;
            ReadOnlySpan<ComponentSnapshotUndoPayload> payload = CollectionsMarshal.AsSpan(_undoComponentSnapshotPayload);
            for (int i = 0; i < count; i++)
            {
                ref readonly ComponentSnapshotUndoPayload entry = ref payload[start + i];
                if (entry.Before == null || entry.After == null)
                {
                    continue;
                }

                allSame &= entry.Before.AsSpan().SequenceEqual(entry.After);
            }

            if (allSame)
            {
                if (start + count == _undoComponentSnapshotPayload.Count)
                {
                    _undoComponentSnapshotPayload.RemoveRange(start, count);
                }

                _pendingEdit = default;
                return;
            }
        }
        else if (_pendingEdit.PendingRecord.Kind == UndoRecordKind.SetFillComponent)
        {
            if (_pendingEdit.PendingRecord.FillBefore.Equals(_pendingEdit.PendingRecord.FillAfter))
            {
                _pendingEdit = default;
                return;
            }
        }
        _undoStack.Push(_pendingEdit.PendingRecord);
        _workspace.BumpSelectedPrefabRevision();
        _pendingEdit = default;
    }

    private int BeginOrUpdateComponentSnapshotEdit(int widgetId, uint entityStableId, AnyComponentHandle component)
    {
        if (_pendingEdit.IsActive &&
            _pendingEdit.WidgetId == widgetId &&
            _pendingEdit.PendingRecord.Kind == UndoRecordKind.SetComponentSnapshot)
        {
            int existingIndex = _pendingEdit.PendingRecord.ComponentSnapshotIndex;
            if ((uint)existingIndex < (uint)_undoComponentSnapshotPayload.Count)
            {
                ref ComponentSnapshotUndoPayload existing = ref CollectionsMarshal.AsSpan(_undoComponentSnapshotPayload)[existingIndex];
                if (existing.EntityStableId == entityStableId && existing.ComponentKind == component.Kind)
                {
                    return existingIndex;
                }
            }
        }

        if (_pendingEdit.IsActive)
        {
            CommitPendingEdit();
        }

        int size = UiComponentClipboardPacker.GetSnapshotSize(component.Kind);
        if (size <= 0)
        {
            return -1;
        }

        var before = new byte[size];
        var after = new byte[size];
        UiComponentClipboardPacker.TryPack(_workspace.PropertyWorld, component, before);
        Buffer.BlockCopy(before, 0, after, 0, size);

        int payloadIndex = _undoComponentSnapshotPayload.Count;
        _undoComponentSnapshotPayload.Add(new ComponentSnapshotUndoPayload
        {
            EntityStableId = entityStableId,
            ComponentKind = component.Kind,
            Before = before,
            After = after
        });

        _pendingEdit = new PendingEdit
        {
            WidgetId = widgetId,
            PendingRecord = UndoRecord.ForComponentSnapshot(payloadIndex),
            IsActive = true
        };

        return payloadIndex;
    }

    private int BeginOrUpdateComponentSnapshotManyEdit(int widgetId, uint entityStableId, ReadOnlySpan<AnyComponentHandle> components)
    {
        if (components.IsEmpty)
        {
            return -1;
        }

        if (_pendingEdit.IsActive &&
            _pendingEdit.WidgetId == widgetId &&
            _pendingEdit.PendingRecord.Kind == UndoRecordKind.SetComponentSnapshotMany)
        {
            int existingStart = _pendingEdit.PendingRecord.PayloadStart;
            int existingCount = _pendingEdit.PendingRecord.PayloadCount;
            if (existingStart >= 0 && existingCount == components.Length && existingStart + existingCount <= _undoComponentSnapshotPayload.Count)
            {
                bool matches = true;
                ReadOnlySpan<ComponentSnapshotUndoPayload> payload = CollectionsMarshal.AsSpan(_undoComponentSnapshotPayload);
                for (int i = 0; i < existingCount; i++)
                {
                    ref readonly ComponentSnapshotUndoPayload entry = ref payload[existingStart + i];
                    if (entry.EntityStableId != entityStableId || entry.ComponentKind != components[i].Kind)
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    return existingStart;
                }
            }
        }

        if (_pendingEdit.IsActive)
        {
            CommitPendingEdit();
        }

        int payloadStart = _undoComponentSnapshotPayload.Count;
        for (int i = 0; i < components.Length; i++)
        {
            AnyComponentHandle component = components[i];
            int size = UiComponentClipboardPacker.GetSnapshotSize(component.Kind);
            if (size <= 0)
            {
                return -1;
            }

            var before = new byte[size];
            var after = new byte[size];
            UiComponentClipboardPacker.TryPack(_workspace.PropertyWorld, component, before);
            Buffer.BlockCopy(before, 0, after, 0, size);

            _undoComponentSnapshotPayload.Add(new ComponentSnapshotUndoPayload
            {
                EntityStableId = entityStableId,
                ComponentKind = component.Kind,
                Before = before,
                After = after
            });
        }

        _pendingEdit = new PendingEdit
        {
            WidgetId = widgetId,
            PendingRecord = UndoRecord.ForComponentSnapshotMany(payloadStart, components.Length),
            IsActive = true
        };

        return payloadStart;
    }

    private int BeginOrUpdateComponentSnapshotManyEdit(int widgetId, ReadOnlySpan<ComponentSnapshotTarget> targets)
    {
        if (targets.IsEmpty)
        {
            return -1;
        }

        if (_pendingEdit.IsActive &&
            _pendingEdit.WidgetId == widgetId &&
            _pendingEdit.PendingRecord.Kind == UndoRecordKind.SetComponentSnapshotMany)
        {
            int existingStart = _pendingEdit.PendingRecord.PayloadStart;
            int existingCount = _pendingEdit.PendingRecord.PayloadCount;
            if (existingStart >= 0 && existingCount == targets.Length && existingStart + existingCount <= _undoComponentSnapshotPayload.Count)
            {
                bool matches = true;
                ReadOnlySpan<ComponentSnapshotUndoPayload> payload = CollectionsMarshal.AsSpan(_undoComponentSnapshotPayload);
                for (int i = 0; i < existingCount; i++)
                {
                    ref readonly ComponentSnapshotUndoPayload entry = ref payload[existingStart + i];
                    if (entry.EntityStableId != targets[i].EntityStableId || entry.ComponentKind != targets[i].Component.Kind)
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    return existingStart;
                }
            }
        }

        if (_pendingEdit.IsActive)
        {
            CommitPendingEdit();
        }

        int payloadStart = _undoComponentSnapshotPayload.Count;
        for (int i = 0; i < targets.Length; i++)
        {
            uint entityStableId = targets[i].EntityStableId;
            AnyComponentHandle component = targets[i].Component;
            if (entityStableId == 0 || !component.IsValid)
            {
                return -1;
            }

            int size = UiComponentClipboardPacker.GetSnapshotSize(component.Kind);
            if (size <= 0)
            {
                return -1;
            }

            var before = new byte[size];
            var after = new byte[size];
            UiComponentClipboardPacker.TryPack(_workspace.PropertyWorld, component, before);
            Buffer.BlockCopy(before, 0, after, 0, size);

            _undoComponentSnapshotPayload.Add(new ComponentSnapshotUndoPayload
            {
                EntityStableId = entityStableId,
                ComponentKind = component.Kind,
                Before = before,
                After = after
            });
        }

        _pendingEdit = new PendingEdit
        {
            WidgetId = widgetId,
            PendingRecord = UndoRecord.ForComponentSnapshotMany(payloadStart, targets.Length),
            IsActive = true
        };

        return payloadStart;
    }

    private void UpdateComponentSnapshotManyAfter(int payloadStart, ReadOnlySpan<AnyComponentHandle> components)
    {
        if (payloadStart < 0 || components.IsEmpty)
        {
            return;
        }

        for (int i = 0; i < components.Length; i++)
        {
            UpdateComponentSnapshotAfter(payloadStart + i, components[i]);
        }
    }

    private void UpdateComponentSnapshotManyAfter(int payloadStart, ReadOnlySpan<ComponentSnapshotTarget> targets)
    {
        if (payloadStart < 0 || targets.IsEmpty)
        {
            return;
        }

        for (int i = 0; i < targets.Length; i++)
        {
            UpdateComponentSnapshotAfter(payloadStart + i, targets[i].Component);
        }
    }

    private void UpdateComponentSnapshotAfter(int payloadIndex, AnyComponentHandle component)
    {
        if ((uint)payloadIndex >= (uint)_undoComponentSnapshotPayload.Count)
        {
            return;
        }

        ref ComponentSnapshotUndoPayload payload = ref CollectionsMarshal.AsSpan(_undoComponentSnapshotPayload)[payloadIndex];
        if (payload.ComponentKind != component.Kind)
        {
            return;
        }

        UiComponentClipboardPacker.TryPack(_workspace.PropertyWorld, component, payload.After);
    }

    private void ApplyUndoRecord(in UndoRecord record, bool applyAfterValues)
    {
        switch (record.Kind)
        {
            case UndoRecordKind.SetProperty:
                ApplyPropertyValue(record.Property0, applyAfterValues ? record.After0 : record.Before0);
                return;

            case UndoRecordKind.SetPropertyBatch:
            {
                byte count = record.PropertyCount;
                if (count >= 1)
                {
                    ApplyPropertyValue(record.Property0, applyAfterValues ? record.After0 : record.Before0);
                }
                if (count >= 2)
                {
                    ApplyPropertyValue(record.Property1, applyAfterValues ? record.After1 : record.Before1);
                }
                if (count >= 3)
                {
                    ApplyPropertyValue(record.Property2, applyAfterValues ? record.After2 : record.Before2);
                }
                if (count >= 4)
                {
                    ApplyPropertyValue(record.Property3, applyAfterValues ? record.After3 : record.Before3);
                }
                return;
            }

            case UndoRecordKind.SetPropertyMany:
            {
                int start = record.PayloadStart;
                int count = record.PayloadCount;
                if (start < 0 || count <= 0 || start + count > _undoPropertyPayload.Count)
                {
                    return;
                }

                ReadOnlySpan<PropertyUndoPayload> payload = CollectionsMarshal.AsSpan(_undoPropertyPayload);
                for (int i = 0; i < count; i++)
                {
                    ref readonly PropertyUndoPayload entry = ref payload[start + i];
                    if (entry.Key.Component.IsNull)
                    {
                        continue;
                    }
                    ApplyPropertyValue(entry.Key, applyAfterValues ? entry.After : entry.Before);
                }
                return;
            }

            case UndoRecordKind.SetComponentSnapshot:
                ApplyComponentSnapshot(record, applyAfterValues);
                return;

            case UndoRecordKind.SetComponentSnapshotMany:
                ApplyComponentSnapshotMany(record, applyAfterValues);
                return;

            case UndoRecordKind.SetFillComponent:
                ApplyFillComponent(record, applyAfterValues);
                return;

            case UndoRecordKind.SetPaintComponent:
                ApplyPaintComponent(record, applyAfterValues);
                return;

            case UndoRecordKind.MoveEntity:
                ApplyMoveEntity(record, applyAfterValues);
                return;

            case UndoRecordKind.CreatePrefab:
                ApplyCreatePrefab(record, applyAfterValues);
                return;

            case UndoRecordKind.CreateShape:
                ApplyCreateShape(record, applyAfterValues);
                return;

            case UndoRecordKind.CreateText:
                ApplyCreateText(record, applyAfterValues);
                return;

            case UndoRecordKind.CreateBooleanGroup:
                ApplyCreateBooleanGroup(record, applyAfterValues);
                return;

            case UndoRecordKind.CreateMaskGroup:
                ApplyCreateMaskGroup(record, applyAfterValues);
                return;

            case UndoRecordKind.DeleteShape:
                ApplyCreateShape(record, applyAfterValues: !applyAfterValues);
                return;

            case UndoRecordKind.DeleteText:
                ApplyCreateText(record, applyAfterValues: !applyAfterValues);
                return;

            case UndoRecordKind.DeleteBooleanGroup:
                ApplyCreateBooleanGroup(record, applyAfterValues: !applyAfterValues);
                return;

            case UndoRecordKind.DeleteMaskGroup:
                ApplyCreateMaskGroup(record, applyAfterValues: !applyAfterValues);
                return;

            case UndoRecordKind.DeletePrefab:
                ApplyCreatePrefab(record, applyAfterValues: !applyAfterValues);
                return;

            case UndoRecordKind.CreatePrefabInstance:
                ApplyCreatePrefabInstance(record, applyAfterValues);
                return;

            case UndoRecordKind.DeletePrefabInstance:
                ApplyCreatePrefabInstance(record, applyAfterValues: !applyAfterValues);
                return;

            case UndoRecordKind.SetTransformComponent:
                ApplyTransformComponent(record, applyAfterValues);
                return;

            case UndoRecordKind.ResizeShape:
                ApplyResizeShape(record, applyAfterValues);
                return;

            case UndoRecordKind.MovePrefab:
                ApplyMovePrefab(record, applyAfterValues);
                return;

            case UndoRecordKind.ResizePrefab:
                ApplyResizePrefab(record, applyAfterValues);
                return;

            case UndoRecordKind.ResizePrefabInstance:
                ApplyResizePrefabInstance(record, applyAfterValues);
                return;

            case UndoRecordKind.SetBooleanGroupOpIndex:
                ApplyBooleanGroupOpIndex(record, applyAfterValues);
                return;

            case UndoRecordKind.AddEntitiesToGroup:
                ApplyAddEntitiesToGroup(record, applyAfterValues);
                return;
        }
    }

    private void ApplyComponentSnapshot(in UndoRecord record, bool applyAfterValues)
    {
        int payloadIndex = record.ComponentSnapshotIndex;
        if ((uint)payloadIndex >= (uint)_undoComponentSnapshotPayload.Count)
        {
            return;
        }

        ref ComponentSnapshotUndoPayload payload = ref CollectionsMarshal.AsSpan(_undoComponentSnapshotPayload)[payloadIndex];
        uint stableId = payload.EntityStableId;
        if (stableId == 0)
        {
            return;
        }

        EntityId entity = _workspace.World.GetEntityByStableId(stableId);
        if (entity.IsNull)
        {
            return;
        }

        if (!_workspace.World.TryGetComponent(entity, payload.ComponentKind, out AnyComponentHandle component) || !component.IsValid)
        {
            if (!UiComponentClipboardPacker.TryCreateComponent(_workspace.PropertyWorld, payload.ComponentKind, stableId, out AnyComponentHandle created))
            {
                return;
            }

            component = created;
            _workspace.SetComponentWithStableId(entity, component);
        }

        byte[] bytes = applyAfterValues ? payload.After : payload.Before;
        if (bytes == null || bytes.Length == 0)
        {
            return;
        }

        UiComponentClipboardPacker.TryUnpack(_workspace.PropertyWorld, component, bytes);
    }

    private void ApplyComponentSnapshotMany(in UndoRecord record, bool applyAfterValues)
    {
        int start = record.PayloadStart;
        int count = record.PayloadCount;
        if (start < 0 || count <= 0 || start + count > _undoComponentSnapshotPayload.Count)
        {
            return;
        }

        ReadOnlySpan<ComponentSnapshotUndoPayload> payload = CollectionsMarshal.AsSpan(_undoComponentSnapshotPayload);
        for (int i = 0; i < count; i++)
        {
            ref readonly ComponentSnapshotUndoPayload entry = ref payload[start + i];
            uint stableId = entry.EntityStableId;
            if (stableId == 0)
            {
                continue;
            }

            EntityId entity = _workspace.World.GetEntityByStableId(stableId);
            if (entity.IsNull)
            {
                continue;
            }

            if (!_workspace.World.TryGetComponent(entity, entry.ComponentKind, out AnyComponentHandle component) || !component.IsValid)
            {
                if (!UiComponentClipboardPacker.TryCreateComponent(_workspace.PropertyWorld, entry.ComponentKind, stableId, out AnyComponentHandle created))
                {
                    continue;
                }

                component = created;
                _workspace.SetComponentWithStableId(entity, component);
            }

            byte[] bytes = applyAfterValues ? entry.After : entry.Before;
            if (bytes == null || bytes.Length == 0)
            {
                continue;
            }

            UiComponentClipboardPacker.TryUnpack(_workspace.PropertyWorld, component, bytes);
        }
    }

    private void ApplyFillComponent(in UndoRecord record, bool applyAfterValues)
    {
        FillComponentHandle handle = record.FillHandle;
        if (handle.IsNull)
        {
            return;
        }

        var fillView = FillComponent.Api.FromHandle(_workspace.PropertyWorld, handle);
        if (!fillView.IsAlive)
        {
            return;
        }

        FillComponent value = applyAfterValues ? record.FillAfter : record.FillBefore;
        fillView.Color = value.Color;
        fillView.UseGradient = value.UseGradient;
        fillView.GradientColorA = value.GradientColorA;
        fillView.GradientColorB = value.GradientColorB;
        fillView.GradientMix = value.GradientMix;
        fillView.GradientDirection = value.GradientDirection;
        fillView.GradientStopCount = value.GradientStopCount;
        fillView.GradientStopT0To3 = value.GradientStopT0To3;
        fillView.GradientStopT4To7 = value.GradientStopT4To7;
        fillView.GradientStopColor0 = value.GradientStopColor0;
        fillView.GradientStopColor1 = value.GradientStopColor1;
        fillView.GradientStopColor2 = value.GradientStopColor2;
        fillView.GradientStopColor3 = value.GradientStopColor3;
        fillView.GradientStopColor4 = value.GradientStopColor4;
        fillView.GradientStopColor5 = value.GradientStopColor5;
        fillView.GradientStopColor6 = value.GradientStopColor6;
        fillView.GradientStopColor7 = value.GradientStopColor7;
    }

    private void ApplyPaintComponent(in UndoRecord record, bool applyAfterValues)
    {
        PaintComponentHandle handle = record.PaintHandle;
        if (handle.IsNull)
        {
            return;
        }

        var paintView = PaintComponent.Api.FromHandle(_workspace.PropertyWorld, handle);
        if (!paintView.IsAlive)
        {
            return;
        }

        PaintComponent value = applyAfterValues ? record.PaintAfter : record.PaintBefore;
        ApplyPaintSnapshot(_workspace.PropertyWorld, handle, value);
    }

    private void ApplyMoveEntity(in UndoRecord record, bool applyAfterValues)
    {
        uint entityStableId = record.MoveEntityStableId;
        if (entityStableId == 0)
        {
            return;
        }

        EntityId entity = _workspace.World.GetEntityByStableId(entityStableId);
        if (entity.IsNull)
        {
            return;
        }

        uint targetParentStableId = applyAfterValues ? record.MoveTargetParentStableId : record.MoveSourceParentStableId;
        int insertIndex = applyAfterValues ? record.MoveTargetIndexAfter : record.MoveSourceIndex;

        EntityId newParent = targetParentStableId == 0 ? EntityId.Null : _workspace.World.GetEntityByStableId(targetParentStableId);
        if (newParent.IsNull)
        {
            return;
        }

        EntityId oldParent = _workspace.World.GetParent(entity);
        int oldIndex = -1;
        if (!oldParent.IsNull)
        {
            _workspace.World.TryGetChildIndex(oldParent, entity, out oldIndex);
        }

        _workspace.World.Reparent(entity, oldParent, oldIndex, newParent, insertIndex);

        if (record.HasTransformSnapshot == 0 || record.TransformHandle.IsNull)
        {
            return;
        }

        ApplyTransform(record.TransformHandle, applyAfterValues ? record.TransformAfter : record.TransformBefore);
    }

    private void ApplyCreatePrefab(in UndoRecord record, bool applyAfterValues)
    {
        int prefabId = record.Prefab.Id;
        if (prefabId <= 0)
        {
            return;
        }

        if (applyAfterValues)
        {
            InsertPrefab(record.PrefabListIndex, record.Prefab);

            EntityId prefabEntity = record.PrefabEntityStableId == 0 ? EntityId.Null : _workspace.World.GetEntityByStableId(record.PrefabEntityStableId);
            if (prefabEntity.IsNull)
            {
                return;
            }

            RemoveFromCurrentParentList(prefabEntity);
            _workspace.World.AddChild(EntityId.Null, prefabEntity, record.PrefabInsertIndex);
            UiWorkspace.SetEntityById(_workspace._prefabEntityById, prefabId, prefabEntity);
            return;
        }

        if (record.PrefabEntityStableId != 0)
        {
            EntityId prefabEntity = _workspace.World.GetEntityByStableId(record.PrefabEntityStableId);
            if (!prefabEntity.IsNull)
            {
                RemoveFromCurrentParentList(prefabEntity);
            }
        }

        UiWorkspace.SetEntityById(_workspace._prefabEntityById, prefabId, EntityId.Null);
        RemovePrefabById(prefabId);
    }

    private void ApplyCreateShape(in UndoRecord record, bool applyAfterValues)
    {
        int shapeId = record.Shape.Id;
        if (shapeId <= 0)
        {
            return;
        }

        if (applyAfterValues)
        {
            InsertShape(record.ShapeListIndex, record.Shape);

            EntityId shapeEntity = record.ShapeEntityStableId == 0 ? EntityId.Null : _workspace.World.GetEntityByStableId(record.ShapeEntityStableId);
            if (shapeEntity.IsNull)
            {
                return;
            }

            UiWorkspace.SetEntityById(_workspace._shapeEntityById, shapeId, shapeEntity);

            EntityId parentEntity = record.ShapeParentStableId == 0 ? EntityId.Null : _workspace.World.GetEntityByStableId(record.ShapeParentStableId);
            if (parentEntity.IsNull)
            {
                return;
            }

            RemoveFromCurrentParentList(shapeEntity);
            _workspace.World.AddChild(parentEntity, shapeEntity, record.ShapeInsertIndex);
            return;
        }

        if (record.ShapeEntityStableId != 0)
        {
            EntityId shapeEntity = _workspace.World.GetEntityByStableId(record.ShapeEntityStableId);
            if (!shapeEntity.IsNull)
            {
                RemoveFromCurrentParentList(shapeEntity);
            }
        }

        UiWorkspace.SetEntityById(_workspace._shapeEntityById, shapeId, EntityId.Null);
        RemoveShapeById(shapeId);
    }

    private void ApplyCreateText(in UndoRecord record, bool applyAfterValues)
    {
        int textId = record.Text.Id;
        if (textId <= 0)
        {
            return;
        }

        if (applyAfterValues)
        {
            InsertText(record.TextListIndex, record.Text);

            EntityId textEntity = record.TextEntityStableId == 0 ? EntityId.Null : _workspace.World.GetEntityByStableId(record.TextEntityStableId);
            if (textEntity.IsNull)
            {
                return;
            }

            UiWorkspace.SetEntityById(_workspace._textEntityById, textId, textEntity);

            EntityId parentEntity = record.TextParentStableId == 0 ? EntityId.Null : _workspace.World.GetEntityByStableId(record.TextParentStableId);
            if (parentEntity.IsNull)
            {
                return;
            }

            RemoveFromCurrentParentList(textEntity);
            _workspace.World.AddChild(parentEntity, textEntity, record.TextInsertIndex);
            return;
        }

        if (record.TextEntityStableId != 0)
        {
            EntityId textEntity = _workspace.World.GetEntityByStableId(record.TextEntityStableId);
            if (!textEntity.IsNull)
            {
                RemoveFromCurrentParentList(textEntity);
            }
        }

        UiWorkspace.SetEntityById(_workspace._textEntityById, textId, EntityId.Null);
        RemoveTextById(textId);
    }

    private void ApplyCreatePrefabInstance(in UndoRecord record, bool applyAfterValues)
    {
        uint instanceStableId = record.PrefabInstanceEntityStableId;
        if (instanceStableId == 0)
        {
            return;
        }

        EntityId instanceEntity = _workspace.World.GetEntityByStableId(instanceStableId);
        if (instanceEntity.IsNull)
        {
            return;
        }

        if (applyAfterValues)
        {
            EntityId parentEntity = record.PrefabInstanceParentStableId == 0
                ? EntityId.Null
                : _workspace.World.GetEntityByStableId(record.PrefabInstanceParentStableId);
            if (parentEntity.IsNull)
            {
                return;
            }

            RemoveFromCurrentParentList(instanceEntity);
            _workspace.World.AddChild(parentEntity, instanceEntity, record.PrefabInstanceInsertIndex);
            return;
        }

        RemoveFromCurrentParentList(instanceEntity);
    }

    private void ApplyCreateBooleanGroup(in UndoRecord record, bool applyAfterValues)
    {
        int groupId = record.Group.Id;
        if (groupId <= 0)
        {
            return;
        }

        uint groupStableId = record.GroupEntityStableId;
        uint parentStableId = record.GroupParentStableId;
        if (groupStableId == 0 || parentStableId == 0)
        {
            return;
        }

        EntityId groupEntity = _workspace.World.GetEntityByStableId(groupStableId);
        EntityId parentEntity = _workspace.World.GetEntityByStableId(parentStableId);
        if (groupEntity.IsNull || parentEntity.IsNull)
        {
            return;
        }

        ReadOnlySpan<EntityMoveUndoPayload> blob = CollectionsMarshal.AsSpan(_undoEntityMovePayload);
        int start = record.PayloadStart;
        int count = record.PayloadCount;
        if (start < 0 || count < 0 || start + count > blob.Length)
        {
            return;
        }

        ReadOnlySpan<EntityMoveUndoPayload> payload = blob.Slice(start, count);

        if (applyAfterValues)
        {
            if (!_workspace.TryGetGroupIndexById(groupId, out _))
            {
                InsertGroup(record.GroupListIndex, record.Group);
            }

            RemoveFromCurrentParentList(groupEntity);
            _workspace.World.AddChild(parentEntity, groupEntity, record.GroupInsertIndex);
            UiWorkspace.SetEntityById(_workspace._groupEntityById, groupId, groupEntity);

            for (int i = 0; i < payload.Length; i++)
            {
                uint entityStableId = payload[i].EntityStableId;
                if (entityStableId == 0)
                {
                    continue;
                }

                EntityId entity = _workspace.World.GetEntityByStableId(entityStableId);
                if (entity.IsNull)
                {
                    continue;
                }

                if (!_workspace.World.TryGetChildIndex(parentEntity, entity, out int currentIndex))
                {
                    continue;
                }

                _workspace.TryPreserveWorldTransformOnReparent(entity, parentEntity, groupEntity);
                _workspace.World.Reparent(entity, parentEntity, currentIndex, groupEntity, insertIndex: i);
            }

            _workspace.SelectSingleEntity(groupEntity);
            return;
        }

        for (int i = 0; i < payload.Length; i++)
        {
            uint entityStableId = payload[i].EntityStableId;
            int sourceIndex = payload[i].SourceIndex;
            if (entityStableId == 0)
            {
                continue;
            }

            EntityId entity = _workspace.World.GetEntityByStableId(entityStableId);
            if (entity.IsNull)
            {
                continue;
            }

            if (!_workspace.World.TryGetChildIndex(groupEntity, entity, out int currentIndex))
            {
                continue;
            }

            _workspace.TryPreserveWorldTransformOnReparent(entity, groupEntity, parentEntity);
            _workspace.World.Reparent(entity, groupEntity, currentIndex, parentEntity, sourceIndex);
        }

        RemoveFromCurrentParentList(groupEntity);
        UiWorkspace.SetEntityById(_workspace._groupEntityById, groupId, EntityId.Null);
        RemoveGroupById(groupId);
    }

    private void RemoveFromCurrentParentList(EntityId entity)
    {
        if (entity.IsNull)
        {
            return;
        }

        EntityId parent = _workspace.World.GetParent(entity);
        if (parent.IsNull)
        {
            _workspace.World.RemoveChild(EntityId.Null, entity);
            return;
        }

        _workspace.World.RemoveChild(parent, entity);
    }

    private void ApplyCreateMaskGroup(in UndoRecord record, bool applyAfterValues)
    {
        ApplyCreateBooleanGroup(record, applyAfterValues);
    }

    private void ApplyTransformComponent(in UndoRecord record, bool applyAfterValues)
    {
        if (record.TransformHandle.IsNull)
        {
            return;
        }

        ApplyTransform(record.TransformHandle, applyAfterValues ? record.TransformAfter : record.TransformBefore);
    }

    private void ApplyResizeShape(in UndoRecord record, bool applyAfterValues)
    {
        if (record.TransformHandle.IsNull)
        {
            return;
        }

        ApplyTransform(record.TransformHandle, applyAfterValues ? record.TransformAfter : record.TransformBefore);

        if (record.ResizeShapeKind == 1)
        {
            ApplyRectGeometry(record.RectGeometryHandle, applyAfterValues ? record.RectGeometryAfter : record.RectGeometryBefore);
            return;
        }

        if (record.ResizeShapeKind == 2)
        {
            ApplyCircleGeometry(record.CircleGeometryHandle, applyAfterValues ? record.CircleGeometryAfter : record.CircleGeometryBefore);
        }
    }

    private void ApplyResizePrefabInstance(in UndoRecord record, bool applyAfterValues)
    {
        if (record.TransformHandle.IsNull || record.PrefabCanvasHandle.IsNull)
        {
            return;
        }

        ApplyTransform(record.TransformHandle, applyAfterValues ? record.TransformAfter : record.TransformBefore);
        ApplyPrefabCanvas(record.PrefabCanvasHandle, applyAfterValues ? record.PrefabCanvasAfter : record.PrefabCanvasBefore);

        uint stableId = record.PrefabInstanceEntityStableId;
        if (stableId == 0)
        {
            return;
        }

        EntityId entity = _workspace.World.GetEntityByStableId(stableId);
        if (entity.IsNull)
        {
            return;
        }

        if (!_workspace.World.TryGetComponent(entity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) || !instanceAny.IsValid)
        {
            return;
        }

        var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
        var instance = PrefabInstanceComponent.Api.FromHandle(_workspace.PropertyWorld, instanceHandle);
        if (!instance.IsAlive)
        {
            return;
        }

        instance.CanvasSizeIsOverridden = applyAfterValues
            ? record.PrefabInstanceCanvasSizeIsOverriddenAfter
            : record.PrefabInstanceCanvasSizeIsOverriddenBefore;
    }

    private void ApplyPrefabCanvas(PrefabCanvasComponentHandle handle, in PrefabCanvasComponent value)
    {
        if (handle.IsNull)
        {
            return;
        }

        var view = PrefabCanvasComponent.Api.FromHandle(_workspace.PropertyWorld, handle);
        if (!view.IsAlive)
        {
            return;
        }

        view.Size = value.Size;
    }

    private void ApplyRectGeometry(RectGeometryComponentHandle handle, in RectGeometryComponent value)
    {
        if (handle.IsNull)
        {
            return;
        }

        var view = RectGeometryComponent.Api.FromHandle(_workspace.PropertyWorld, handle);
        if (!view.IsAlive)
        {
            return;
        }

        view.Size = value.Size;
        view.CornerRadius = value.CornerRadius;
    }

    private void ApplyCircleGeometry(CircleGeometryComponentHandle handle, in CircleGeometryComponent value)
    {
        if (handle.IsNull)
        {
            return;
        }

        var view = CircleGeometryComponent.Api.FromHandle(_workspace.PropertyWorld, handle);
        if (!view.IsAlive)
        {
            return;
        }

        view.Radius = value.Radius;
    }

    private void ApplyMovePrefab(in UndoRecord record, bool applyAfterValues)
    {
        if (record.PrefabId <= 0)
        {
            return;
        }

        Vector2 delta = applyAfterValues ? record.PrefabMoveDeltaWorld : -record.PrefabMoveDeltaWorld;
        if (delta.X == 0f && delta.Y == 0f)
        {
            return;
        }

        _workspace.TryMovePrefabById(record.PrefabId, delta);
    }

    private void ApplyResizePrefab(in UndoRecord record, bool applyAfterValues)
    {
        if (record.PrefabId <= 0)
        {
            return;
        }

        ImRect rectWorld = applyAfterValues ? record.PrefabRectWorldAfter : record.PrefabRectWorldBefore;
        _workspace.TrySetPrefabRectWorldById(record.PrefabId, rectWorld);
    }

    private void ApplyBooleanGroupOpIndex(in UndoRecord record, bool applyAfterValues)
    {
        int groupId = record.BooleanGroupId;
        if (groupId <= 0 || !_workspace.TryGetGroupIndexById(groupId, out int groupIndex))
        {
            return;
        }

        int opIndex = applyAfterValues ? record.BooleanGroupOpIndexAfter : record.BooleanGroupOpIndexBefore;
        opIndex = Math.Clamp(opIndex, 0, UiWorkspace.BooleanOpOptions.Length - 1);

        var group = _workspace._groups[groupIndex];
        group.OpIndex = opIndex;
        _workspace._groups[groupIndex] = group;
    }

    private void ApplyAddEntitiesToGroup(in UndoRecord record, bool applyAfterValues)
    {
        uint groupStableId = record.AddToGroupGroupStableId;
        uint parentStableId = record.AddToGroupParentStableId;
        if (groupStableId == 0 || parentStableId == 0)
        {
            return;
        }

        EntityId groupEntity = _workspace.World.GetEntityByStableId(groupStableId);
        EntityId parentEntity = _workspace.World.GetEntityByStableId(parentStableId);
        if (groupEntity.IsNull || parentEntity.IsNull)
        {
            return;
        }

        int movedCount = record.AddToGroupMovedCount;
        if (movedCount <= 0)
        {
            return;
        }

        ReadOnlySpan<EntityMoveUndoPayload> blob = CollectionsMarshal.AsSpan(_undoEntityMovePayload);
        int start = record.PayloadStart;
        int count = record.PayloadCount;
        if (start < 0 || count < 0 || start + count > blob.Length)
        {
            return;
        }

        ReadOnlySpan<EntityMoveUndoPayload> payload = blob.Slice(start, count);
        if (payload.Length < movedCount)
        {
            return;
        }

        if (applyAfterValues)
        {
            int destStartIndex = record.AddToGroupDestStartIndex;
            for (int i = 0; i < movedCount; i++)
            {
                uint entityStableId = payload[i].EntityStableId;
                if (entityStableId == 0)
                {
                    continue;
                }

                EntityId entity = _workspace.World.GetEntityByStableId(entityStableId);
                if (entity.IsNull)
                {
                    continue;
                }

                if (!_workspace.World.TryGetChildIndex(parentEntity, entity, out int currentIndex))
                {
                    continue;
                }

                _workspace.TryPreserveWorldTransformOnReparent(entity, parentEntity, groupEntity);
                _workspace.World.Reparent(entity, parentEntity, currentIndex, groupEntity, destStartIndex + i);
            }
        }
        else
        {
            for (int i = 0; i < movedCount; i++)
            {
                uint entityStableId = payload[i].EntityStableId;
                int sourceIndex = payload[i].SourceIndex;
                if (entityStableId == 0)
                {
                    continue;
                }

                EntityId entity = _workspace.World.GetEntityByStableId(entityStableId);
                if (entity.IsNull)
                {
                    continue;
                }

                if (!_workspace.World.TryGetChildIndex(groupEntity, entity, out int currentIndex))
                {
                    continue;
                }

                _workspace.TryPreserveWorldTransformOnReparent(entity, groupEntity, parentEntity);
                _workspace.World.Reparent(entity, groupEntity, currentIndex, parentEntity, sourceIndex);
            }
        }
    }

    private void ApplyTransform(TransformComponentHandle handle, in TransformComponent value)
    {
        if (handle.IsNull)
        {
            return;
        }

        var view = TransformComponent.Api.FromHandle(_workspace.PropertyWorld, handle);
        if (!view.IsAlive)
        {
            return;
        }

        view.Position = value.Position;
        view.Scale = value.Scale;
        view.Rotation = value.Rotation;
        view.Anchor = value.Anchor;
        view.Depth = value.Depth;
    }

    private bool TryGetEntityTransformSnapshot(EntityId entity, out TransformComponentHandle handle, out TransformComponent snapshot)
    {
        handle = default;
        snapshot = default;

        if (entity.IsNull)
        {
            return false;
        }

        if (!_workspace.World.TryGetComponent(entity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny))
        {
            return false;
        }

        handle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
        return TryGetTransformSnapshot(handle, out snapshot);
    }

    private bool TryGetTransformSnapshot(TransformComponentHandle handle, out TransformComponent snapshot)
    {
        snapshot = default;
        if (handle.IsNull)
        {
            return false;
        }

        var view = TransformComponent.Api.FromHandle(_workspace.PropertyWorld, handle);
        if (!view.IsAlive)
        {
            return false;
        }

        snapshot = new TransformComponent
        {
            Position = view.Position,
            Scale = view.Scale,
            Rotation = view.Rotation,
            Anchor = view.Anchor,
            Depth = view.Depth
        };
        return true;
    }

    private void InsertPrefab(int insertIndex, in UiWorkspace.Prefab prefab)
    {
        if (insertIndex < 0 || insertIndex > _workspace._prefabs.Count)
        {
            insertIndex = _workspace._prefabs.Count;
        }

        _workspace._prefabs.Insert(insertIndex, prefab);
        if (_workspace._nextPrefabId <= prefab.Id)
        {
            _workspace._nextPrefabId = prefab.Id + 1;
        }

        for (int i = insertIndex; i < _workspace._prefabs.Count; i++)
        {
            UiWorkspace.SetIndexById(_workspace._prefabIndexById, _workspace._prefabs[i].Id, i);
        }

        if (_workspace._selectedPrefabIndex >= insertIndex)
        {
            _workspace._selectedPrefabIndex++;
        }
    }

    private void RemovePrefabById(int prefabId)
    {
        if (!_workspace.TryGetPrefabIndexById(prefabId, out int prefabIndex))
        {
            return;
        }

        var prefab = _workspace._prefabs[prefabIndex];
        if (UiWorkspace.TryGetEntityById(_workspace._prefabEntityById, prefabId, out EntityId prefabEntity) && !prefabEntity.IsNull &&
            _workspace.World.GetChildCount(prefabEntity) != 0)
        {
            return;
        }

        _workspace._prefabs.RemoveAt(prefabIndex);
        UiWorkspace.SetIndexById(_workspace._prefabIndexById, prefabId, -1);
        UiWorkspace.SetEntityById(_workspace._prefabEntityById, prefabId, EntityId.Null);

        for (int i = prefabIndex; i < _workspace._prefabs.Count; i++)
        {
            UiWorkspace.SetIndexById(_workspace._prefabIndexById, _workspace._prefabs[i].Id, i);
        }

        if (_workspace._selectedPrefabIndex == prefabIndex)
        {
            if (_workspace._prefabs.Count == 0)
            {
                _workspace._selectedPrefabIndex = -1;
            }
            else
            {
                _workspace._selectedPrefabIndex = Math.Min(prefabIndex, _workspace._prefabs.Count - 1);
            }
        }
        else if (_workspace._selectedPrefabIndex > prefabIndex)
        {
            _workspace._selectedPrefabIndex--;
        }
    }

    private void InsertShape(int insertIndex, in UiWorkspace.Shape shape)
    {
        if (insertIndex < 0 || insertIndex > _workspace._shapes.Count)
        {
            insertIndex = _workspace._shapes.Count;
        }

        _workspace._shapes.Insert(insertIndex, shape);
        if (_workspace._nextShapeId <= shape.Id)
        {
            _workspace._nextShapeId = shape.Id + 1;
        }

        for (int i = insertIndex; i < _workspace._shapes.Count; i++)
        {
            UiWorkspace.SetIndexById(_workspace._shapeIndexById, _workspace._shapes[i].Id, i);
        }
    }

    private void RemoveShapeById(int shapeId)
    {
        if (!_workspace.TryGetShapeIndexById(shapeId, out int shapeIndex))
        {
            return;
        }

        var shape = _workspace._shapes[shapeIndex];
        UiWorkspace.TryGetEntityById(_workspace._shapeEntityById, shapeId, out EntityId shapeEntity);
        UiWorkspace.SetEntityById(_workspace._shapeEntityById, shapeId, EntityId.Null);

        _workspace._shapes.RemoveAt(shapeIndex);
        UiWorkspace.SetIndexById(_workspace._shapeIndexById, shapeId, -1);

        for (int i = shapeIndex; i < _workspace._shapes.Count; i++)
        {
            UiWorkspace.SetIndexById(_workspace._shapeIndexById, _workspace._shapes[i].Id, i);
        }

        RemoveSelectedEntity(shapeEntity);
    }

    private void InsertText(int insertIndex, in UiWorkspace.Text text)
    {
        if (insertIndex < 0 || insertIndex > _workspace._texts.Count)
        {
            insertIndex = _workspace._texts.Count;
        }

        _workspace._texts.Insert(insertIndex, text);
        if (_workspace._nextTextId <= text.Id)
        {
            _workspace._nextTextId = text.Id + 1;
        }

        for (int i = insertIndex; i < _workspace._texts.Count; i++)
        {
            UiWorkspace.SetIndexById(_workspace._textIndexById, _workspace._texts[i].Id, i);
        }
    }

    private void RemoveTextById(int textId)
    {
        if (!_workspace.TryGetTextIndexById(textId, out int textIndex))
        {
            return;
        }

        var text = _workspace._texts[textIndex];
        UiWorkspace.TryGetEntityById(_workspace._textEntityById, textId, out EntityId textEntity);
        UiWorkspace.SetEntityById(_workspace._textEntityById, textId, EntityId.Null);

        _workspace._texts.RemoveAt(textIndex);
        UiWorkspace.SetIndexById(_workspace._textIndexById, textId, -1);

        for (int i = textIndex; i < _workspace._texts.Count; i++)
        {
            UiWorkspace.SetIndexById(_workspace._textIndexById, _workspace._texts[i].Id, i);
        }

        RemoveSelectedEntity(textEntity);
    }

    private void InsertGroup(int insertIndex, in UiWorkspace.BooleanGroup group)
    {
        if (insertIndex < 0 || insertIndex > _workspace._groups.Count)
        {
            insertIndex = _workspace._groups.Count;
        }

        _workspace._groups.Insert(insertIndex, group);
        if (_workspace._nextGroupId <= group.Id)
        {
            _workspace._nextGroupId = group.Id + 1;
        }

        for (int i = insertIndex; i < _workspace._groups.Count; i++)
        {
            UiWorkspace.SetIndexById(_workspace._groupIndexById, _workspace._groups[i].Id, i);
        }
    }

    private void RemoveGroupById(int groupId)
    {
        if (!_workspace.TryGetGroupIndexById(groupId, out int groupIndex))
        {
            return;
        }

        UiWorkspace.TryGetEntityById(_workspace._groupEntityById, groupId, out EntityId groupEntity);

        _workspace._groups.RemoveAt(groupIndex);
        UiWorkspace.SetIndexById(_workspace._groupIndexById, groupId, -1);
        UiWorkspace.SetEntityById(_workspace._groupEntityById, groupId, EntityId.Null);

        for (int i = groupIndex; i < _workspace._groups.Count; i++)
        {
            UiWorkspace.SetIndexById(_workspace._groupIndexById, _workspace._groups[i].Id, i);
        }

        RemoveSelectedEntity(groupEntity);
    }

    private void RemoveSelectedEntity(EntityId entity)
    {
        if (entity.IsNull)
        {
            return;
        }

        for (int i = 0; i < _workspace._selectedEntities.Count; i++)
        {
            if (_workspace._selectedEntities[i].Value == entity.Value)
            {
                _workspace._selectedEntities.RemoveAt(i);
                return;
            }
        }
    }

    private void RemovePolygonPoints(int pointStart, int pointCount)
    {
        if (pointCount <= 0)
        {
            return;
        }

        int end = pointStart + pointCount;
        if (pointStart < 0 || end != _workspace._polygonPointsLocal.Count)
        {
            return;
        }

        _workspace._polygonPointsLocal.RemoveRange(pointStart, pointCount);
    }

    private void ApplyPropertyValue(in PropertyKey key, in PropertyValue value)
    {
        if (!TryResolveSlot(key, out PropertySlot slot))
        {
            return;
        }

        WriteValue(_workspace.PropertyWorld, slot, value);
    }

    private static PropertyValue ReadValue(Pooled.Runtime.IPoolRegistry reg, in PropertySlot slot)
    {
        return slot.Kind switch
        {
            PropertyKind.Float => PropertyValue.FromFloat(PropertyDispatcher.ReadFloat(reg, slot)),
            PropertyKind.Int => PropertyValue.FromInt(PropertyDispatcher.ReadInt(reg, slot)),
            PropertyKind.Bool => PropertyValue.FromBool(PropertyDispatcher.ReadBool(reg, slot)),
            PropertyKind.Vec2 => PropertyValue.FromVec2(PropertyDispatcher.ReadVec2(reg, slot)),
            PropertyKind.Vec3 => PropertyValue.FromVec3(PropertyDispatcher.ReadVec3(reg, slot)),
            PropertyKind.Vec4 => PropertyValue.FromVec4(PropertyDispatcher.ReadVec4(reg, slot)),
            PropertyKind.Color32 => PropertyValue.FromColor32(PropertyDispatcher.ReadColor32(reg, slot)),
            PropertyKind.StringHandle => PropertyValue.FromStringHandle(PropertyDispatcher.ReadStringHandle(reg, slot)),
            PropertyKind.Fixed64 => PropertyValue.FromFixed64(PropertyDispatcher.ReadFixed64(reg, slot)),
            PropertyKind.Fixed64Vec2 => PropertyValue.FromFixed64Vec2(PropertyDispatcher.ReadFixed64Vec2(reg, slot)),
            PropertyKind.Fixed64Vec3 => PropertyValue.FromFixed64Vec3(PropertyDispatcher.ReadFixed64Vec3(reg, slot)),
            _ => default
        };
    }

    private static void WriteValue(Pooled.Runtime.IPoolRegistry reg, in PropertySlot slot, in PropertyValue value)
    {
        switch (slot.Kind)
        {
            case PropertyKind.Float:
                PropertyDispatcher.WriteFloat(reg, slot, value.Float);
                break;
            case PropertyKind.Int:
                PropertyDispatcher.WriteInt(reg, slot, value.Int);
                break;
            case PropertyKind.Bool:
                PropertyDispatcher.WriteBool(reg, slot, value.Bool);
                break;
            case PropertyKind.Vec2:
                PropertyDispatcher.WriteVec2(reg, slot, value.Vec2);
                break;
            case PropertyKind.Vec3:
                PropertyDispatcher.WriteVec3(reg, slot, value.Vec3);
                break;
            case PropertyKind.Vec4:
                PropertyDispatcher.WriteVec4(reg, slot, value.Vec4);
                break;
            case PropertyKind.Color32:
                PropertyDispatcher.WriteColor32(reg, slot, value.Color32);
                break;
            case PropertyKind.StringHandle:
                PropertyDispatcher.WriteStringHandle(reg, slot, value.StringHandle);
                break;
            case PropertyKind.Fixed64:
                PropertyDispatcher.WriteFixed64(reg, slot, value.Fixed64);
                break;
            case PropertyKind.Fixed64Vec2:
                PropertyDispatcher.WriteFixed64Vec2(reg, slot, value.Fixed64Vec2);
                break;
            case PropertyKind.Fixed64Vec3:
                PropertyDispatcher.WriteFixed64Vec3(reg, slot, value.Fixed64Vec3);
                break;
        }
    }

    private bool TryResolveSlot(in PropertyKey key, out PropertySlot slot)
    {
        slot = default;

        AnyComponentHandle component = key.Component;
        if (component.IsNull)
        {
            return false;
        }

        int propertyCount = PropertyDispatcher.GetPropertyCount(component);
        if (propertyCount <= 0)
        {
            return false;
        }

        int hintIndex = key.PropertyIndexHint;
        if (hintIndex >= 0 && hintIndex < propertyCount)
        {
            if (PropertyDispatcher.TryGetInfo(component, hintIndex, out var hintInfo) && hintInfo.PropertyId == key.PropertyId)
            {
                slot = PropertyDispatcher.GetSlot(component, hintIndex);
                return slot.Kind == key.Kind;
            }
        }

        for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
        {
            if (!PropertyDispatcher.TryGetInfo(component, propertyIndex, out var info))
            {
                continue;
            }

            if (info.PropertyId != key.PropertyId)
            {
                continue;
            }

            slot = PropertyDispatcher.GetSlot(component, propertyIndex);
            return slot.Kind == key.Kind;
        }

        return false;
    }

    private bool TryGetAnchorBoundsSizeLocal(EntityId entity, out Vector2 sizeLocal)
    {
        sizeLocal = Vector2.Zero;
        if (entity.IsNull)
        {
            return false;
        }

        UiNodeType type = _workspace.World.GetNodeType(entity);
        if (type == UiNodeType.Text)
        {
            if (!_workspace.World.TryGetComponent(entity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectAny))
            {
                return false;
            }

            var rectHandle = new RectGeometryComponentHandle(rectAny.Index, rectAny.Generation);
            var rectGeometry = RectGeometryComponent.Api.FromHandle(_workspace.PropertyWorld, rectHandle);
            if (!rectGeometry.IsAlive)
            {
                return false;
            }

            sizeLocal = rectGeometry.Size;
            return true;
        }

        if (type == UiNodeType.Prefab)
        {
            if (!_workspace.World.TryGetComponent(entity, PrefabCanvasComponent.Api.PoolIdConst, out AnyComponentHandle canvasAny))
            {
                return false;
            }

            var canvasHandle = new PrefabCanvasComponentHandle(canvasAny.Index, canvasAny.Generation);
            var canvas = PrefabCanvasComponent.Api.FromHandle(_workspace.PropertyWorld, canvasHandle);
            if (!canvas.IsAlive)
            {
                return false;
            }

            sizeLocal = canvas.Size;
            return true;
        }

        if (type != UiNodeType.Shape)
        {
            return false;
        }

        if (_workspace.World.TryGetComponent(entity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectGeometryAny))
        {
            var rectHandle = new RectGeometryComponentHandle(rectGeometryAny.Index, rectGeometryAny.Generation);
            var rectGeometry = RectGeometryComponent.Api.FromHandle(_workspace.PropertyWorld, rectHandle);
            if (!rectGeometry.IsAlive)
            {
                return false;
            }

            sizeLocal = rectGeometry.Size;
            return true;
        }

        if (_workspace.World.TryGetComponent(entity, CircleGeometryComponent.Api.PoolIdConst, out AnyComponentHandle circleAny))
        {
            var circleHandle = new CircleGeometryComponentHandle(circleAny.Index, circleAny.Generation);
            var circle = CircleGeometryComponent.Api.FromHandle(_workspace.PropertyWorld, circleHandle);
            if (!circle.IsAlive)
            {
                return false;
            }

            float diameter = circle.Radius * 2f;
            sizeLocal = new Vector2(diameter, diameter);
            return true;
        }

        if (_workspace.World.TryGetComponent(entity, PathComponent.Api.PoolIdConst, out AnyComponentHandle pathAny))
        {
            var pathHandle = new PathComponentHandle(pathAny.Index, pathAny.Generation);
            var path = PathComponent.Api.FromHandle(_workspace.PropertyWorld, pathHandle);
            if (!path.IsAlive)
            {
                return false;
            }

            int vertexCount = path.VertexCount;
            if (vertexCount <= 0 || vertexCount > PathComponent.MaxVertices)
            {
                return false;
            }

            ReadOnlySpan<Vector2> positionsLocal = path.PositionLocalSpan().Slice(0, vertexCount);
            ReadOnlySpan<Vector2> tangentsInLocal = path.TangentInLocalSpan().Slice(0, vertexCount);
            ReadOnlySpan<Vector2> tangentsOutLocal = path.TangentOutLocalSpan().Slice(0, vertexCount);

            float minX = positionsLocal[0].X;
            float minY = positionsLocal[0].Y;
            float maxX = minX;
            float maxY = minY;

            for (int i = 0; i < vertexCount; i++)
            {
                Vector2 p = positionsLocal[i];
                if (p.X < minX) { minX = p.X; }
                if (p.Y < minY) { minY = p.Y; }
                if (p.X > maxX) { maxX = p.X; }
                if (p.Y > maxY) { maxY = p.Y; }

                Vector2 handleIn = p + tangentsInLocal[i];
                if (handleIn.X < minX) { minX = handleIn.X; }
                if (handleIn.Y < minY) { minY = handleIn.Y; }
                if (handleIn.X > maxX) { maxX = handleIn.X; }
                if (handleIn.Y > maxY) { maxY = handleIn.Y; }

                Vector2 handleOut = p + tangentsOutLocal[i];
                if (handleOut.X < minX) { minX = handleOut.X; }
                if (handleOut.Y < minY) { minY = handleOut.Y; }
                if (handleOut.X > maxX) { maxX = handleOut.X; }
                if (handleOut.Y > maxY) { maxY = handleOut.Y; }
            }

            sizeLocal = new Vector2(maxX - minX, maxY - minY);
            return sizeLocal.X > 0.0001f && sizeLocal.Y > 0.0001f;
        }

        return false;
    }

    private static Vector2 RotateVector(Vector2 value, float radians)
    {
        if (radians == 0f)
        {
            return value;
        }

        float sin = MathF.Sin(radians);
        float cos = MathF.Cos(radians);
        return new Vector2(
            value.X * cos - value.Y * sin,
            value.X * sin + value.Y * cos);
    }
}
