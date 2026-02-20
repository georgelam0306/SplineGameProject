using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using DerpLib.Text;
using Property.Runtime;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    private Font? _textRenderFontOverride;
    internal bool IsRuntimeMode { get; private set; }

    private readonly List<EntityId> _runtimeModeEntities = new(capacity: 1024);
    private RuntimeModeSnapshot? _runtimeModeSnapshot;

    internal void SetTextRenderFont(Font font)
    {
        _textRenderFontOverride = font;
    }

    private Font GetTextRenderFont()
    {
        if (_textRenderFontOverride != null)
        {
            return _textRenderFontOverride;
        }

        Font? font = DerpLib.ImGui.Im.Context.Font;
        if (font is null)
        {
            throw new InvalidOperationException("No font set for text rendering. Call UiRuntime.SetFont(...) or initialize ImGui fonts.");
        }

        return font;
    }

    internal void PrepareForRuntimeAfterInstantiate()
    {
        // Pre-size editor-only stable-id lists so render loops don't allocate.
        uint maxStableId = 0;
        int entityCount = _world.EntityCount;
        for (int entityIndex = 1; entityIndex <= entityCount; entityIndex++)
        {
            uint stableId = _world.GetStableId(new EntityId(entityIndex));
            if (stableId > maxStableId)
            {
                maxStableId = stableId;
            }
        }

        EnsureStableIdListSize(_editorLayersDocument.ExpandedByStableId, maxStableId, defaultValue: (byte)0);
        EnsureStableIdListSize(_editorLayersDocument.HiddenByStableId, maxStableId, defaultValue: (byte)0);
        EnsureStableIdListSize(_editorLayersDocument.LockedByStableId, maxStableId, defaultValue: (byte)0);
        EnsureStableIdListSize(_editorLayersDocument.NameByStableId, maxStableId, defaultValue: string.Empty);

        // Pre-warm text layout cache objects so the render loop stays allocation-free.
        for (int entityIndex = 1; entityIndex <= entityCount; entityIndex++)
        {
            EntityId entity = new EntityId(entityIndex);
            if (_world.GetNodeType(entity) == UiNodeType.None)
            {
                continue;
            }

            if (!_world.TryGetComponent(entity, TextComponent.Api.PoolIdConst, out AnyComponentHandle textAny) || !textAny.IsValid)
            {
                continue;
            }

            _ = GetOrCreateTextLayoutCache(textAny);
        }

        // Pre-size runtime lists so per-frame traversal does not allocate.
        _runtimeModeEntities.EnsureCapacity(entityCount + 1);

        // Standalone runtimes (UiRuntime) rely on runtime-mode state to drive input/event triggers.
        IsRuntimeMode = true;
        ResetRuntimeEventState();

        StateMachineRuntimeSystem.EnsureRuntimeComponents(this);
    }

    internal void ResetStandaloneRuntimeEventState()
    {
        ResetRuntimeEventState();
    }

    internal void TickRuntime(EntityId prefabEntity, uint deltaMicroseconds)
    {
        if (prefabEntity.IsNull || _world.GetNodeType(prefabEntity) != UiNodeType.Prefab)
        {
            return;
        }

        EnsureRuntimePrefabVariableStore(prefabEntity);
        TickRuntimeEvents(prefabEntity);

        if (deltaMicroseconds != 0)
        {
            CollectEntitySubtree(prefabEntity, _runtimeModeEntities);
            StateMachineRuntimeSystem.TickScoped(this, deltaMicroseconds, _runtimeModeEntities, filterDefinitionOwnerStableId: 0, filterMachineId: 0);
        }
        EndFrameClearRuntimeTriggers();
    }

    internal void ToggleRuntimeMode()
    {
        if (IsRuntimeMode)
        {
            ExitRuntimeMode();
            return;
        }

        EnterRuntimeMode();
    }

    private void EnterRuntimeMode()
    {
        if (IsRuntimeMode)
        {
            return;
        }

        Commands.FlushPendingEdits();
        SyncActiveAnimationDocumentToLibrary();

        if (!TryGetActivePrefabEntity(out EntityId prefabEntity) || prefabEntity.IsNull)
        {
            ShowToast("No active prefab");
            return;
        }

        CollectEntitySubtree(prefabEntity, _runtimeModeEntities);

        _runtimeModeSnapshot = new RuntimeModeSnapshot();
        _runtimeModeSnapshot.Capture(this, _runtimeModeEntities);

        // Ensure runtime components exist for entities in the active prefab subtree.
        StateMachineRuntimeSystem.EnsureRuntimeComponentsScoped(this, _runtimeModeEntities);

        ResetRuntimeEventState();
        EnsureRuntimePrefabVariableStore(prefabEntity);

        IsRuntimeMode = true;
    }

    private void ExitRuntimeMode()
    {
        if (!IsRuntimeMode)
        {
            return;
        }

        if (!TryGetActivePrefabEntity(out EntityId prefabEntity) || prefabEntity.IsNull)
        {
            IsRuntimeMode = false;
            _runtimeModeSnapshot = null;
            return;
        }

        CollectEntitySubtree(prefabEntity, _runtimeModeEntities);

        RuntimeModeSnapshot? snapshot = _runtimeModeSnapshot;
        if (snapshot != null)
        {
            snapshot.Restore(this);
        }

        // Remove runtime-only components so they don't get serialized.
        RemoveRuntimeComponents(_runtimeModeEntities);
        RemoveRuntimePrefabVariableStore(prefabEntity);
        ResetRuntimeEventState();

        _runtimeModeSnapshot = null;
        IsRuntimeMode = false;
        RebuildCanvasSurfaceFromLastSize();
    }

    private void RemoveRuntimeComponents(List<EntityId> entities)
    {
        for (int i = 0; i < entities.Count; i++)
        {
            EntityId entity = entities[i];

            if (_world.TryGetComponent(entity, StateMachineRuntimeComponent.Api.PoolIdConst, out AnyComponentHandle runtimeAny) && runtimeAny.IsValid)
            {
                var handle = new StateMachineRuntimeComponentHandle(runtimeAny.Index, runtimeAny.Generation);
                var view = StateMachineRuntimeComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    StateMachineRuntimeComponent.Api.Destroy(_propertyWorld, view);
                }
                _world.RemoveComponent(entity, StateMachineRuntimeComponent.Api.PoolIdConst);
            }

            if (_world.TryGetComponent(entity, AnimationTargetResolutionRuntimeComponent.Api.PoolIdConst, out AnyComponentHandle targetAny) && targetAny.IsValid)
            {
                var handle = new AnimationTargetResolutionRuntimeComponentHandle(targetAny.Index, targetAny.Generation);
                var view = AnimationTargetResolutionRuntimeComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    AnimationTargetResolutionRuntimeComponent.Api.Destroy(_propertyWorld, view);
                }
                _world.RemoveComponent(entity, AnimationTargetResolutionRuntimeComponent.Api.PoolIdConst);
            }

            if (_world.TryGetComponent(entity, AnimationBasePoseRuntimeComponent.Api.PoolIdConst, out AnyComponentHandle basePoseAny) && basePoseAny.IsValid)
            {
                var handle = new AnimationBasePoseRuntimeComponentHandle(basePoseAny.Index, basePoseAny.Generation);
                var view = AnimationBasePoseRuntimeComponent.Api.FromHandle(_propertyWorld, handle);
                if (view.IsAlive)
                {
                    AnimationBasePoseRuntimeComponent.Api.Destroy(_propertyWorld, view);
                }
                _world.RemoveComponent(entity, AnimationBasePoseRuntimeComponent.Api.PoolIdConst);
            }
        }
    }

    internal void TickRuntimeMode(float deltaSeconds)
    {
        if (!IsRuntimeMode)
        {
            return;
        }

        if (!TryGetActivePrefabEntity(out EntityId prefabEntity) || prefabEntity.IsNull)
        {
            return;
        }

        uint deltaUs = deltaSeconds <= 0f || !float.IsFinite(deltaSeconds)
            ? 0u
            : (uint)Math.Clamp((int)MathF.Round(deltaSeconds * 1_000_000f), 0, int.MaxValue);

        EnsureRuntimePrefabVariableStore(prefabEntity);
        TickRuntimeEvents(prefabEntity);

        if (deltaUs != 0)
        {
            CollectEntitySubtree(prefabEntity, _runtimeModeEntities);

            uint filterDefinitionOwnerStableId = 0;
            ushort filterMachineId = 0;
            if (AnimationEditorWindow.IsStateMachineMode() &&
                TryGetActiveStateMachineDefinition(out var def) &&
                StateMachineDefinitionOps.TryEnsureSelectedMachineId(def, out ushort selectedMachineId) &&
                selectedMachineId != 0)
            {
                filterDefinitionOwnerStableId = _world.GetStableId(prefabEntity);
                filterMachineId = selectedMachineId;
            }

            StateMachineRuntimeSystem.TickScoped(this, deltaUs, _runtimeModeEntities, filterDefinitionOwnerStableId, filterMachineId);
        }
        EndFrameClearRuntimeTriggers();
    }

    private void CollectEntitySubtree(EntityId root, List<EntityId> entities)
    {
        entities.Clear();
        if (root.IsNull)
        {
            return;
        }

        entities.Add(root);
        for (int i = 0; i < entities.Count; i++)
        {
            EntityId current = entities[i];
            ReadOnlySpan<EntityId> children = _world.GetChildren(current);
            for (int childIndex = 0; childIndex < children.Length; childIndex++)
            {
                entities.Add(children[childIndex]);
            }
        }
    }

    internal void BuildRuntimeUi(CanvasSdfDrawList draw, EntityId rootPrefab, int width, int height)
    {
        if (rootPrefab.IsNull)
        {
            return;
        }

        draw.PushClipRect(0f, 0f, width, height);
        draw.DrawRect(0f, 0f, width, height, color: 0u);
        // Runtime usually builds the authored prefab root's children (prefab node is a container).
        // If a compiled asset (or selection) points at a non-prefab node, render that node directly so
        // runtimes don't silently draw nothing.
        if (_world.GetNodeType(rootPrefab) == UiNodeType.Prefab)
        {
            bool pushedRootClipRect = TryPushEntityClipRectEcs(draw, rootPrefab, Vector2.Zero, IdentityWorldTransform);
            int pushedRootWarps = TryPushEntityWarpStackEcs(draw, rootPrefab);
            BuildPrefabChildrenEcs(draw, rootPrefab, Vector2.Zero);
            for (int popIndex = 0; popIndex < pushedRootWarps; popIndex++)
            {
                draw.PopWarp();
            }

            if (pushedRootClipRect)
            {
                draw.PopClipRect();
            }
        }
        else
        {
            BuildEntityRecursiveEcs(draw, rootPrefab, Vector2.Zero, IdentityWorldTransform);
        }
        draw.PopClipRect();
    }

    private sealed class RuntimeModeSnapshot
    {
        private readonly List<Entry> _entries = new(capacity: 4096);

        public void Capture(UiWorkspace workspace, List<EntityId> entities)
        {
            _entries.Clear();

            byte[] scratch = ArrayPool<byte>.Shared.Rent(256);
            try
            {
                for (int entityIndex = 0; entityIndex < entities.Count; entityIndex++)
                {
                    EntityId entity = entities[entityIndex];
                    uint stableId = workspace._world.GetStableId(entity);
                    if (stableId == 0)
                    {
                        continue;
                    }

                    ulong presentMask = workspace._world.GetComponentPresentMask(entity);
                    ReadOnlySpan<AnyComponentHandle> slots = workspace._world.GetComponentSlots(entity);

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

                        if (scratch.Length < size)
                        {
                            ArrayPool<byte>.Shared.Return(scratch);
                            scratch = ArrayPool<byte>.Shared.Rent(size);
                        }

                        Span<byte> packed = scratch.AsSpan(0, size);
                        if (!UiComponentClipboardPacker.TryPack(workspace._propertyWorld, component, packed))
                        {
                            continue;
                        }

                        byte[] bytes = new byte[size];
                        packed.CopyTo(bytes);
                        _entries.Add(new Entry(stableId, component.Kind, bytes));
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scratch);
            }
        }

        public void Restore(UiWorkspace workspace)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];

                EntityId entity = workspace._world.GetEntityByStableId(entry.EntityStableId);
                if (entity.IsNull)
                {
                    continue;
                }

                if (!workspace._world.TryGetComponent(entity, entry.ComponentKind, out AnyComponentHandle any) || !any.IsValid)
                {
                    continue;
                }

                if (!UiComponentClipboardPacker.TryUnpack(workspace._propertyWorld, any, entry.Bytes))
                {
                    continue;
                }
            }
        }

        private readonly struct Entry
        {
            public readonly uint EntityStableId;
            public readonly ushort ComponentKind;
            public readonly byte[] Bytes;

            public Entry(uint entityStableId, ushort componentKind, byte[] bytes)
            {
                EntityStableId = entityStableId;
                ComponentKind = componentKind;
                Bytes = bytes;
            }
        }
    }
}
