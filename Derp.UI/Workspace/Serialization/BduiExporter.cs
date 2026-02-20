using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DerpLib.AssetPipeline;
using Property.Runtime;

namespace Derp.UI;

internal static class BduiExporter
{
    private const uint PayloadMagic = 0x49554442; // "BDUI"
    private const int PayloadVersion = 2;
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static void ExportWorkspace(UiWorkspace workspace, string path)
    {
        if (workspace == null) throw new ArgumentNullException(nameof(workspace));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));

        workspace.Commands.FlushPendingEdits();

        workspace.PreparePrefabInstancesForExport();

        var prefabs = new List<EntityId>(capacity: 64);
        ReadOnlySpan<EntityId> rootChildren = workspace.World.GetChildren(EntityId.Null);
        for (int i = 0; i < rootChildren.Length; i++)
        {
            EntityId prefabEntity = rootChildren[i];
            if (workspace.World.GetNodeType(prefabEntity) == UiNodeType.Prefab)
            {
                prefabs.Add(prefabEntity);
            }
        }

        if (prefabs.Count == 0)
        {
            throw new InvalidOperationException("Workspace contains no prefabs to export.");
        }

        var nodes = new List<EntityId>(capacity: 4096);
        var stableIds = new List<uint>(capacity: 4096);
        var childCounts = new List<int>(capacity: 4096);
        var prefabRootNodeIndex = new List<int>(capacity: prefabs.Count);
        BuildWorkspacePreorder(workspace, prefabs, nodes, stableIds, childCounts, prefabRootNodeIndex);

        var stringTable = new StringTableBuilder();
        UiDocumentStringPatcher.CollectStringsForSave(workspace, stringTable);
        CollectPrefabNamesForBdui(workspace, prefabs, stringTable);

        byte[] payload = BuildPayload(workspace, prefabs, prefabRootNodeIndex, nodes, stableIds, childCounts, stringTable);
        byte[] fileBytes = ChunkHeader.Write(typeof(CompiledUi), payload);
        File.WriteAllBytes(path, fileBytes);
    }

    private static void CollectPrefabNamesForBdui(UiWorkspace workspace, List<EntityId> prefabs, StringTableBuilder strings)
    {
        for (int i = 0; i < prefabs.Count; i++)
        {
            uint stableId = workspace.World.GetStableId(prefabs[i]);
            if (stableId != 0 && workspace.TryGetLayerName(stableId, out string name) && !string.IsNullOrEmpty(name))
            {
                _ = strings.GetOrAdd(name);
            }
        }
    }

    private static byte[] BuildPayload(
        UiWorkspace workspace,
        List<EntityId> prefabs,
        List<int> prefabRootNodeIndex,
        List<EntityId> nodes,
        List<uint> stableIds,
        List<int> childCounts,
        StringTableBuilder stringTable)
    {
        using var ms = new MemoryStream(capacity: 64 * 1024);
        using var writer = new BinaryWriter(ms, Utf8, leaveOpen: true);

        writer.Write(PayloadMagic);
        writer.Write(PayloadVersion);

        IReadOnlyList<string> strings = stringTable.Strings;
        writer.Write(strings.Count);
        for (int i = 0; i < strings.Count; i++)
        {
            string str = strings[i] ?? string.Empty;
            byte[] bytes = Utf8.GetBytes(str);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        int expectedStringCount = strings.Count;

        writer.Write(prefabs.Count);
        for (int prefabIndex = 0; prefabIndex < prefabs.Count; prefabIndex++)
        {
            EntityId prefabEntity = prefabs[prefabIndex];
            uint stableId = workspace.World.GetStableId(prefabEntity);
            int rootIndex = prefabRootNodeIndex[prefabIndex];

            uint nameIndex = 0;
            if (stableId != 0 && workspace.TryGetLayerName(stableId, out string name) && !string.IsNullOrEmpty(name))
            {
                nameIndex = stringTable.GetOrAdd(name);
            }

            writer.Write(stableId);
            writer.Write((int)nameIndex);
            writer.Write(rootIndex);
        }

        writer.Write(nodes.Count);
        if (stringTable.Strings.Count != expectedStringCount)
        {
            throw new InvalidOperationException("Bdui exporter string table changed after writing; prefab name collection is incomplete.");
        }

        byte[] scratch = ArrayPool<byte>.Shared.Rent(256);
        try
        {
            for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                EntityId entity = nodes[nodeIndex];
                UiNodeType type = nodeIndex == 0 ? UiNodeType.None : workspace.World.GetNodeType(entity);
                writer.Write(stableIds[nodeIndex]);
                writer.Write((byte)type);
                writer.Write(childCounts[nodeIndex]);

                int componentCount = 0;
                ulong presentMask = 0;
                ReadOnlySpan<AnyComponentHandle> slots = default;
                if (!entity.IsNull)
                {
                    presentMask = workspace.World.GetComponentPresentMask(entity);
                    slots = workspace.World.GetComponentSlots(entity);

                    for (int slotIndex = 0; slotIndex < UiComponentSlotMap.MaxSlots; slotIndex++)
                    {
                        if (((presentMask >> slotIndex) & 1UL) == 0)
                        {
                            continue;
                        }

                        AnyComponentHandle component = slots[slotIndex];
                        if (!component.IsValid || !IsRuntimeComponentKind(component.Kind))
                        {
                            continue;
                        }

                        int snapshotSize = UiComponentClipboardPacker.GetSnapshotSize(component.Kind);
                        if (snapshotSize <= 0)
                        {
                            continue;
                        }

                        componentCount++;
                    }
                }

                if (componentCount > ushort.MaxValue)
                {
                    throw new InvalidOperationException("Too many components on node.");
                }

                writer.Write((ushort)componentCount);

                for (int slotIndex = 0; slotIndex < UiComponentSlotMap.MaxSlots && !entity.IsNull; slotIndex++)
                {
                    if (((presentMask >> slotIndex) & 1UL) == 0)
                    {
                        continue;
                    }

                    AnyComponentHandle component = slots[slotIndex];
                    if (!component.IsValid || !IsRuntimeComponentKind(component.Kind))
                    {
                        continue;
                    }

                    int snapshotSize = UiComponentClipboardPacker.GetSnapshotSize(component.Kind);
                    if (snapshotSize <= 0)
                    {
                        continue;
                    }

                    if (scratch.Length < snapshotSize)
                    {
                        ArrayPool<byte>.Shared.Return(scratch);
                        scratch = ArrayPool<byte>.Shared.Rent(snapshotSize);
                    }

                    Span<byte> bytes = scratch.AsSpan(0, snapshotSize);
                    if (!UiComponentClipboardPacker.TryPack(workspace.PropertyWorld, component, bytes))
                    {
                        throw new InvalidOperationException("Failed to pack component.");
                    }

                    UiDocumentStringPatcher.PatchPackedComponentForSave(workspace, entity, component.Kind, bytes, stringTable);
                    if (stringTable.Strings.Count != expectedStringCount)
                    {
                        throw new InvalidOperationException("Bdui exporter string table changed after pre-collection; missing string collection for some component.");
                    }

                    writer.Write(component.Kind);
                    writer.Write(snapshotSize);
                    writer.Write(bytes);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }

        writer.Flush();
        return ms.ToArray();
    }

    private static bool IsRuntimeComponentKind(ushort kind)
    {
        return kind == TransformComponent.Api.PoolIdConst ||
            kind == ShapeComponent.Api.PoolIdConst ||
            kind == RectGeometryComponent.Api.PoolIdConst ||
            kind == CircleGeometryComponent.Api.PoolIdConst ||
            kind == FillComponent.Api.PoolIdConst ||
            kind == StrokeComponent.Api.PoolIdConst ||
            kind == GlowComponent.Api.PoolIdConst ||
            kind == BooleanGroupComponent.Api.PoolIdConst ||
            kind == TextComponent.Api.PoolIdConst ||
            kind == BlendComponent.Api.PoolIdConst ||
            kind == PathComponent.Api.PoolIdConst ||
            kind == PrefabCanvasComponent.Api.PoolIdConst ||
            kind == PaintComponent.Api.PoolIdConst ||
            kind == MaskGroupComponent.Api.PoolIdConst ||
            kind == ClipRectComponent.Api.PoolIdConst ||
            kind == ConstraintListComponent.Api.PoolIdConst ||
            kind == ModifierStackComponent.Api.PoolIdConst ||
            kind == DraggableComponent.Api.PoolIdConst ||
            kind == EventListenerComponent.Api.PoolIdConst ||
            kind == PrefabInstanceComponent.Api.PoolIdConst ||
            kind == PrefabVariablesComponent.Api.PoolIdConst ||
            kind == PrefabBindingsComponent.Api.PoolIdConst ||
            kind == PrefabRevisionComponent.Api.PoolIdConst ||
            kind == PrefabInstancePropertyOverridesComponent.Api.PoolIdConst ||
            kind == PrefabListDataComponent.Api.PoolIdConst ||
            kind == AnimationLibraryComponent.Api.PoolIdConst ||
            kind == StateMachineDefinitionComponent.Api.PoolIdConst;
    }

    private static void BuildWorkspacePreorder(
        UiWorkspace workspace,
        List<EntityId> prefabs,
        List<EntityId> nodes,
        List<uint> stableIds,
        List<int> childCounts,
        List<int> prefabRootNodeIndex)
    {
        nodes.Clear();
        stableIds.Clear();
        childCounts.Clear();
        prefabRootNodeIndex.Clear();

        // Virtual root node: children are prefabs.
        nodes.Add(EntityId.Null);
        stableIds.Add(0);
        childCounts.Add(prefabs.Count);

        for (int i = 0; i < prefabs.Count; i++)
        {
            prefabRootNodeIndex.Add(nodes.Count);
            AddAuthoredNode(workspace, prefabs[i], nodes, stableIds, childCounts);
        }
    }

    private static void AddAuthoredNode(UiWorkspace workspace, EntityId entity, List<EntityId> nodes, List<uint> stableIds, List<int> childCounts)
    {
        int nodeIndex = nodes.Count;
        nodes.Add(entity);
        stableIds.Add(workspace.World.GetStableId(entity));
        childCounts.Add(0);

        ReadOnlySpan<EntityId> children = workspace.World.GetChildren(entity);
        int authoredChildCount = 0;

        for (int i = 0; i < children.Length; i++)
        {
            EntityId child = children[i];

            // Skip expanded prefab-instance clones/proxies; export only authored hierarchy.
            if (workspace.World.TryGetComponent(child, PrefabExpandedComponent.Api.PoolIdConst, out AnyComponentHandle expandedAny) && expandedAny.IsValid)
            {
                continue;
            }

            authoredChildCount++;
        }

        childCounts[nodeIndex] = authoredChildCount;

        for (int i = 0; i < children.Length; i++)
        {
            EntityId child = children[i];
            if (workspace.World.TryGetComponent(child, PrefabExpandedComponent.Api.PoolIdConst, out AnyComponentHandle expandedAny) && expandedAny.IsValid)
            {
                continue;
            }

            AddAuthoredNode(workspace, child, nodes, stableIds, childCounts);
        }
    }
}
