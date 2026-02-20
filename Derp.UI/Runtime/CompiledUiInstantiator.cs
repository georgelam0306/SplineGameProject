using System;
using System.Buffers;
using System.Collections.Generic;
using Pooled.Runtime;
using Property.Runtime;

namespace Derp.UI;

internal static class CompiledUiInstantiator
{
    public static void InstantiateWorkspace(UiWorkspace workspace, CompiledUi compiledUi, out EntityId[] entitiesByNodeIndex)
    {
        if (workspace is null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        if (compiledUi is null)
        {
            throw new ArgumentNullException(nameof(compiledUi));
        }

        UiWorld world = workspace.World;
        World propertyWorld = workspace.PropertyWorld;

        CompiledUi.Node[] nodes = compiledUi.Nodes;
        int nodeCount = nodes.Length;
        if (nodeCount <= 0)
        {
            throw new InvalidOperationException("Compiled UI contains no nodes.");
        }

        entitiesByNodeIndex = new EntityId[nodeCount];
        entitiesByNodeIndex[0] = EntityId.Null;

        int[] parentNodeIndexStack = new int[nodeCount];
        int[] remainingChildrenStack = new int[nodeCount];
        int stackCount = 1;
        parentNodeIndexStack[0] = 0;
        remainingChildrenStack[0] = nodes[0].ChildCount;

        for (int nodeIndex = 1; nodeIndex < nodeCount; nodeIndex++)
        {
            while (stackCount > 0 && remainingChildrenStack[stackCount - 1] == 0)
            {
                stackCount--;
            }

            if (stackCount <= 0)
            {
                throw new InvalidOperationException("Invalid compiled UI tree (multiple roots).");
            }

            int frameIndex = stackCount - 1;
            int parentNodeIndex = parentNodeIndexStack[frameIndex];
            EntityId parentEntity = entitiesByNodeIndex[parentNodeIndex];

            var node = nodes[nodeIndex];

            EntityId entity;
            uint stableId = node.StableId;
            int insertIndex = world.GetChildCount(parentEntity);
            if (stableId == 0)
            {
                entity = world.CreateEntity(node.NodeType, parentEntity);
            }
            else
            {
                entity = world.CreateEntityWithStableId(node.NodeType, parentEntity, stableId, insertIndex);
            }
            entitiesByNodeIndex[nodeIndex] = entity;

            remainingChildrenStack[frameIndex]--;

            int childCount = node.ChildCount;
            if (childCount > 0)
            {
                parentNodeIndexStack[stackCount] = nodeIndex;
                remainingChildrenStack[stackCount] = childCount;
                stackCount++;
            }
        }

        while (stackCount > 0 && remainingChildrenStack[stackCount - 1] == 0)
        {
            stackCount--;
        }

        if (stackCount != 0)
        {
            throw new InvalidOperationException("Invalid compiled UI tree (truncated nodes).");
        }

        var deferredInstances = new List<(EntityId Entity, AnyComponentHandle Component, byte[] Bytes)>(capacity: 16);

        byte[] scratch = ArrayPool<byte>.Shared.Rent(256);
        try
        {
            for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
            {
                EntityId entity = entitiesByNodeIndex[nodeIndex];
                var node = nodes[nodeIndex];

                if (entity.IsNull)
                {
                    continue;
                }

                int firstComponentIndex = node.FirstComponentIndex;
                int componentCount = node.ComponentCount;

                for (int i = 0; i < componentCount; i++)
                {
                    CompiledUi.Component srcComponent = compiledUi.Components[firstComponentIndex + i];
                    ushort kind = srcComponent.Kind;

                    int snapshotSize = UiComponentClipboardPacker.GetSnapshotSize(kind);
                    if (snapshotSize <= 0)
                    {
                        throw new InvalidOperationException($"Unsupported component kind {kind} in compiled UI.");
                    }

                    if (srcComponent.Bytes.Length != snapshotSize)
                    {
                        throw new InvalidOperationException($"Schema mismatch for component kind {kind}: expected {snapshotSize} bytes, got {srcComponent.Bytes.Length}.");
                    }

                    if (scratch.Length < snapshotSize)
                    {
                        ArrayPool<byte>.Shared.Return(scratch);
                        scratch = ArrayPool<byte>.Shared.Rent(snapshotSize);
                    }

                    ReadOnlySpan<byte> srcBytes = srcComponent.Bytes;
                    Span<byte> dstBytes = scratch.AsSpan(0, snapshotSize);
                    srcBytes.CopyTo(dstBytes);

                    uint stableId = world.GetStableId(entity);
                    if (!UiComponentClipboardPacker.TryCreateComponent(propertyWorld, kind, stableId, out AnyComponentHandle createdComponent) || !createdComponent.IsValid)
                    {
                        throw new InvalidOperationException($"Failed to create component kind {kind} while instantiating compiled UI.");
                    }

                    world.SetComponent(entity, createdComponent);

                    if (kind == PrefabInstanceComponent.Api.PoolIdConst)
                    {
                        var saved = new byte[snapshotSize];
                        dstBytes.CopyTo(saved);
                        deferredInstances.Add((entity, createdComponent, saved));
                        continue;
                    }

                    UiDocumentStringPatcher.UnpatchPackedComponentForLoad(workspace, entity, kind, dstBytes, compiledUi.Strings);

                    if (!UiComponentClipboardPacker.TryUnpack(propertyWorld, createdComponent, dstBytes))
                    {
                        throw new InvalidOperationException($"Failed to unpack component kind {kind} while instantiating compiled UI.");
                    }
                }
            }

            for (int i = 0; i < deferredInstances.Count; i++)
            {
                (EntityId entity, AnyComponentHandle component, byte[] bytes) = deferredInstances[i];

                UiDocumentStringPatcher.UnpatchPackedComponentForLoad(workspace, entity, PrefabInstanceComponent.Api.PoolIdConst, bytes, compiledUi.Strings);

                if (!UiComponentClipboardPacker.TryUnpack(propertyWorld, component, bytes))
                {
                    throw new InvalidOperationException($"Failed to unpack component kind {PrefabInstanceComponent.Api.PoolIdConst} while instantiating compiled UI.");
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }
}
