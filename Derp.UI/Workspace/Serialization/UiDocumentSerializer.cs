using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Property.Runtime;

namespace Derp.UI;

internal static class UiDocumentSerializer
{
    private const int CurrentVersion = 2;
    private const uint Magic = 0x46495544; // "DUIF"

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static void SaveToFile(UiWorkspace workspace, string path)
    {
        if (workspace == null) throw new ArgumentNullException(nameof(workspace));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));

        var stringTable = new StringTableBuilder();
        UiDocumentStringPatcher.CollectStringsForSave(workspace, stringTable);

        var nodeList = new List<EntityId>(capacity: 1024);
        BuildPreorderNodeList(workspace.World, nodeList);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);

        writer.Write(Magic);
        writer.Write(CurrentVersion);

        WriteStringTableChunk(writer, stringTable.Strings);
        WritePrefabIdChunk(writer, workspace);
        WriteTreeChunk(writer, workspace.World);
        WriteComponentsChunk(writer, workspace, nodeList, stringTable);
        WriteEditorLayersChunk(writer, workspace, stringTable.Strings);
    }

    public static void LoadFromFile(UiWorkspace workspace, string path)
    {
        if (workspace == null) throw new ArgumentNullException(nameof(workspace));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);

        uint magic = reader.ReadUInt32();
        if (magic != Magic)
        {
            throw new InvalidOperationException("Not a .dui file.");
        }

        int version = reader.ReadInt32();
        if (version <= 0 || version > CurrentVersion)
        {
            throw new InvalidOperationException("Unsupported .dui version.");
        }

        ReadOnlySpan<char> expectedStrings = "STRS".AsSpan();
        ReadOnlySpan<char> expectedPrefabs = "PFID".AsSpan();
        ReadOnlySpan<char> expectedTree = "TREE".AsSpan();
        ReadOnlySpan<char> expectedComponents = "CMPS".AsSpan();
        ReadOnlySpan<char> expectedLayers = "LAYR".AsSpan();

        var stringTable = ReadStringTableChunk(reader, expectedStrings);
        var prefabIdByStableId = ReadPrefabIdChunk(reader, expectedPrefabs);

        workspace.ResetDocumentForLoad();

        var nodeList = new List<EntityId>(capacity: 1024);
        ReadTreeChunk(reader, expectedTree, workspace.World, nodeList);

        var componentBlobs = ReadComponentsChunk(reader, expectedComponents, workspace, nodeList, stringTable, version);
        ApplyComponents(workspace, componentBlobs, stringTable);

        ReadEditorLayersChunk(reader, expectedLayers, workspace, stringTable);

        workspace.RebuildLegacyCachesAfterLoad(prefabIdByStableId);
    }

    private static void BuildPreorderNodeList(UiWorld world, List<EntityId> nodes)
    {
        nodes.Clear();

        ReadOnlySpan<EntityId> rootChildren = world.GetChildren(EntityId.Null);
        for (int i = 0; i < rootChildren.Length; i++)
        {
            EntityId child = rootChildren[i];
            if (IsExpandedPrefabClone(world, child))
            {
                continue;
            }
            AddPreorder(world, child, nodes);
        }
    }

    private static void AddPreorder(UiWorld world, EntityId entity, List<EntityId> nodes)
    {
        if (entity.IsNull)
        {
            return;
        }

        // Skip expanded prefab-instance clones/proxies; save only authored hierarchy.
        if (IsExpandedPrefabClone(world, entity))
        {
            return;
        }

        nodes.Add(entity);
        ReadOnlySpan<EntityId> children = world.GetChildren(entity);
        for (int i = 0; i < children.Length; i++)
        {
            EntityId child = children[i];
            if (IsExpandedPrefabClone(world, child))
            {
                continue;
            }
            AddPreorder(world, child, nodes);
        }
    }

    private static void WriteChunk(BinaryWriter writer, ReadOnlySpan<char> chunkId, int chunkVersion, Action<BinaryWriter> writePayload)
    {
        if (chunkId.Length != 4)
        {
            throw new ArgumentException("Chunk id must be 4 chars.");
        }

        uint id = ((uint)chunkId[0]) |
            ((uint)chunkId[1] << 8) |
            ((uint)chunkId[2] << 16) |
            ((uint)chunkId[3] << 24);

        writer.Write(id);
        writer.Write(chunkVersion);

        long lengthPos = writer.BaseStream.Position;
        writer.Write(0);

        long startPos = writer.BaseStream.Position;
        writePayload(writer);
        long endPos = writer.BaseStream.Position;

        int length = checked((int)(endPos - startPos));
        long restorePos = writer.BaseStream.Position;
        writer.BaseStream.Position = lengthPos;
        writer.Write(length);
        writer.BaseStream.Position = restorePos;
    }

    private static void ExpectChunk(BinaryReader reader, ReadOnlySpan<char> expectedId, out int chunkVersion, out int payloadLength)
    {
        uint id = reader.ReadUInt32();
        chunkVersion = reader.ReadInt32();
        payloadLength = reader.ReadInt32();

        uint expected = ((uint)expectedId[0]) |
            ((uint)expectedId[1] << 8) |
            ((uint)expectedId[2] << 16) |
            ((uint)expectedId[3] << 24);

        if (id != expected)
        {
            throw new InvalidOperationException("Unexpected .dui chunk order.");
        }

        if (payloadLength < 0)
        {
            throw new InvalidOperationException("Invalid chunk length.");
        }
    }

    private static void WriteStringTableChunk(BinaryWriter writer, IReadOnlyList<string> strings)
    {
        WriteChunk(writer, "STRS".AsSpan(), chunkVersion: 1, writePayload: w =>
        {
            w.Write(strings.Count);
            for (int i = 0; i < strings.Count; i++)
            {
                string str = strings[i] ?? string.Empty;
                byte[] bytes = Utf8.GetBytes(str);
                w.Write(bytes.Length);
                w.Write(bytes);
            }
        });
    }

    private static IReadOnlyList<string> ReadStringTableChunk(BinaryReader reader, ReadOnlySpan<char> expectedId)
    {
        ExpectChunk(reader, expectedId, out int chunkVersion, out int payloadLength);
        if (chunkVersion != 1)
        {
            throw new InvalidOperationException("Unsupported STRS chunk version.");
        }

        long start = reader.BaseStream.Position;

        int count = reader.ReadInt32();
        if (count < 1)
        {
            throw new InvalidOperationException("String table is missing.");
        }

        var strings = new List<string>(capacity: count);
        for (int i = 0; i < count; i++)
        {
            int len = reader.ReadInt32();
            if (len < 0)
            {
                throw new InvalidOperationException("Invalid string length.");
            }

            byte[] bytes = reader.ReadBytes(len);
            if (bytes.Length != len)
            {
                throw new EndOfStreamException();
            }

            strings.Add(len == 0 ? string.Empty : Utf8.GetString(bytes));
        }

        EnsureChunkConsumed(reader, start, payloadLength);
        return strings;
    }

    private static void WritePrefabIdChunk(BinaryWriter writer, UiWorkspace workspace)
    {
        WriteChunk(writer, "PFID".AsSpan(), chunkVersion: 1, writePayload: w =>
        {
            int count = workspace._prefabs.Count;
            w.Write(count);

            for (int prefabIndex = 0; prefabIndex < count; prefabIndex++)
            {
                int prefabId = workspace._prefabs[prefabIndex].Id;
                if (!UiWorkspace.TryGetEntityById(workspace._prefabEntityById, prefabId, out EntityId prefabEntity) || prefabEntity.IsNull)
                {
                    continue;
                }

                uint stableId = workspace.World.GetStableId(prefabEntity);
                if (stableId == 0)
                {
                    continue;
                }

                w.Write(stableId);
                w.Write(prefabId);
            }
        });
    }

    private static Dictionary<uint, int> ReadPrefabIdChunk(BinaryReader reader, ReadOnlySpan<char> expectedId)
    {
        ExpectChunk(reader, expectedId, out int chunkVersion, out int payloadLength);
        if (chunkVersion != 1)
        {
            throw new InvalidOperationException("Unsupported PFID chunk version.");
        }

        long start = reader.BaseStream.Position;

        int count = reader.ReadInt32();
        if (count < 0)
        {
            throw new InvalidOperationException("Invalid PFID entry count.");
        }

        var map = new Dictionary<uint, int>(capacity: Math.Max(16, count));

        while (reader.BaseStream.Position - start < payloadLength)
        {
            uint stableId = reader.ReadUInt32();
            int prefabId = reader.ReadInt32();
            if (stableId == 0 || prefabId <= 0)
            {
                continue;
            }
            map[stableId] = prefabId;
        }

        EnsureChunkConsumed(reader, start, payloadLength);
        return map;
    }

    private static void WriteTreeChunk(BinaryWriter writer, UiWorld world)
    {
        WriteChunk(writer, "TREE".AsSpan(), chunkVersion: 1, writePayload: w =>
        {
            ReadOnlySpan<EntityId> rootChildren = world.GetChildren(EntityId.Null);
            int rootCount = 0;
            for (int i = 0; i < rootChildren.Length; i++)
            {
                if (!IsExpandedPrefabClone(world, rootChildren[i]))
                {
                    rootCount++;
                }
            }

            w.Write(rootCount);
            for (int i = 0; i < rootChildren.Length; i++)
            {
                EntityId child = rootChildren[i];
                if (IsExpandedPrefabClone(world, child))
                {
                    continue;
                }
                WriteTreeNode(w, world, child);
            }
        });
    }

    private static void ReadTreeChunk(BinaryReader reader, ReadOnlySpan<char> expectedId, UiWorld world, List<EntityId> nodeList)
    {
        ExpectChunk(reader, expectedId, out int chunkVersion, out int payloadLength);
        if (chunkVersion != 1)
        {
            throw new InvalidOperationException("Unsupported TREE chunk version.");
        }

        long start = reader.BaseStream.Position;

        int rootCount = reader.ReadInt32();
        if (rootCount < 0)
        {
            throw new InvalidOperationException("Invalid TREE root count.");
        }

        nodeList.Clear();
        for (int i = 0; i < rootCount; i++)
        {
            ReadTreeNode(reader, world, EntityId.Null, insertIndex: i, nodeList);
        }

        EnsureChunkConsumed(reader, start, payloadLength);
    }

    private static void WriteTreeNode(BinaryWriter writer, UiWorld world, EntityId entity)
    {
        uint stableId = world.GetStableId(entity);
        if (stableId == 0)
        {
            throw new InvalidOperationException("Entity is missing StableId.");
        }

        UiNodeType type = world.GetNodeType(entity);
        ReadOnlySpan<EntityId> children = world.GetChildren(entity);

        writer.Write(stableId);
        writer.Write((byte)type);
        int childCount = 0;
        for (int i = 0; i < children.Length; i++)
        {
            if (!IsExpandedPrefabClone(world, children[i]))
            {
                childCount++;
            }
        }
        writer.Write(childCount);

        for (int i = 0; i < children.Length; i++)
        {
            EntityId child = children[i];
            if (IsExpandedPrefabClone(world, child))
            {
                continue;
            }
            WriteTreeNode(writer, world, child);
        }
    }

    private static bool IsExpandedPrefabClone(UiWorld world, EntityId entity)
    {
        return world.TryGetComponent(entity, PrefabExpandedComponent.Api.PoolIdConst, out AnyComponentHandle expandedAny) &&
            expandedAny.IsValid;
    }

    private static void ReadTreeNode(BinaryReader reader, UiWorld world, EntityId parent, int insertIndex, List<EntityId> nodeList)
    {
        uint stableId = reader.ReadUInt32();
        UiNodeType type = (UiNodeType)reader.ReadByte();
        int childCount = reader.ReadInt32();
        if (stableId == 0)
        {
            throw new InvalidOperationException("TREE node has invalid StableId.");
        }
        if (childCount < 0)
        {
            throw new InvalidOperationException("TREE node has invalid child count.");
        }

        EntityId entity = world.CreateEntityWithStableId(type, parent, stableId, insertIndex);
        nodeList.Add(entity);

        for (int i = 0; i < childCount; i++)
        {
            ReadTreeNode(reader, world, entity, insertIndex: i, nodeList);
        }
    }

    private readonly struct ComponentBlob
    {
        public readonly EntityId Entity;
        public readonly AnyComponentHandle Component;
        public readonly ushort Kind;
        public readonly byte[] Bytes;

        public ComponentBlob(EntityId entity, AnyComponentHandle component, ushort kind, byte[] bytes)
        {
            Entity = entity;
            Component = component;
            Kind = kind;
            Bytes = bytes;
        }
    }

    private static void WriteComponentsChunk(BinaryWriter writer, UiWorkspace workspace, List<EntityId> nodeList, StringTableBuilder stringTable)
    {
        WriteChunk(writer, "CMPS".AsSpan(), chunkVersion: 1, writePayload: w =>
        {
            w.Write(nodeList.Count);

            byte[] scratch = ArrayPool<byte>.Shared.Rent(256);
            try
            {
                for (int nodeIndex = 0; nodeIndex < nodeList.Count; nodeIndex++)
                {
                    EntityId entity = nodeList[nodeIndex];
                    uint stableId = workspace.World.GetStableId(entity);
                    w.Write(stableId);

                    ulong presentMask = workspace.World.GetComponentPresentMask(entity);
                    ReadOnlySpan<AnyComponentHandle> slots = workspace.World.GetComponentSlots(entity);

                    int componentCount = 0;
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

                        int snapshotSize = UiComponentClipboardPacker.GetSnapshotSize(component.Kind);
                        if (snapshotSize <= 0)
                        {
                            continue;
                        }

                        componentCount++;
                    }

                    if (componentCount > ushort.MaxValue)
                    {
                        throw new InvalidOperationException("Too many components on entity.");
                    }

                    w.Write((ushort)componentCount);

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

                        w.Write(component.Kind);
                        w.Write(snapshotSize);
                        w.Write(bytes);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scratch);
            }
        });
    }

    private static List<ComponentBlob> ReadComponentsChunk(
        BinaryReader reader,
        ReadOnlySpan<char> expectedId,
        UiWorkspace workspace,
        List<EntityId> nodeList,
        IReadOnlyList<string> stringTable,
        int fileVersion)
    {
        ExpectChunk(reader, expectedId, out int chunkVersion, out int payloadLength);
        if (chunkVersion != 1)
        {
            throw new InvalidOperationException("Unsupported CMPS chunk version.");
        }

        long start = reader.BaseStream.Position;

        int nodeCount = reader.ReadInt32();
        if (nodeCount != nodeList.Count)
        {
            throw new InvalidOperationException("CMPS node count does not match TREE.");
        }

        var blobs = new List<ComponentBlob>(capacity: 1024);

        for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
        {
            uint stableId = reader.ReadUInt32();
            EntityId entity = nodeList[nodeIndex];
            uint expectedStableId = workspace.World.GetStableId(entity);
            if (stableId != expectedStableId)
            {
                throw new InvalidOperationException("CMPS entity order does not match TREE.");
            }

            ushort componentCount = reader.ReadUInt16();
            for (int i = 0; i < componentCount; i++)
            {
                ushort kind = reader.ReadUInt16();
                int snapshotSize = reader.ReadInt32();
                if (snapshotSize <= 0)
                {
                    throw new InvalidOperationException("Invalid component snapshot size.");
                }

                int expectedSize = UiComponentClipboardPacker.GetSnapshotSize(kind);
                bool needsExpand = false;
                if (expectedSize != snapshotSize)
                {
                    if (fileVersion == 1 &&
                        kind == StateMachineDefinitionComponent.Api.PoolIdConst &&
                        snapshotSize > 0 &&
                        snapshotSize < expectedSize)
                    {
                        needsExpand = true;
                    }
                    else
                    {
                        throw new InvalidOperationException("Component schema mismatch.");
                    }
                }

                byte[] bytes = reader.ReadBytes(snapshotSize);
                if (bytes.Length != snapshotSize)
                {
                    throw new EndOfStreamException();
                }

                if (needsExpand)
                {
                    byte[] expanded = new byte[expectedSize];
                    Buffer.BlockCopy(bytes, 0, expanded, 0, bytes.Length);
                    bytes = expanded;
                }

                uint entityStableId = workspace.World.GetStableId(entity);
                if (!UiComponentClipboardPacker.TryCreateComponent(workspace.PropertyWorld, kind, entityStableId, out AnyComponentHandle created) || !created.IsValid)
                {
                    throw new InvalidOperationException("Failed to create component.");
                }

                workspace.SetComponentWithStableId(entity, created);
                blobs.Add(new ComponentBlob(entity, created, kind, bytes));
            }
        }

        EnsureChunkConsumed(reader, start, payloadLength);
        return blobs;
    }

    private static void ApplyComponents(UiWorkspace workspace, List<ComponentBlob> blobs, IReadOnlyList<string> stringTable)
    {
        ApplyComponentsByKind(workspace, blobs, stringTable, PrefabVariablesComponent.Api.PoolIdConst);
        ApplyComponentsByKind(workspace, blobs, stringTable, PrefabInstanceComponent.Api.PoolIdConst);
        ApplyComponentsByKind(workspace, blobs, stringTable, PrefabListDataComponent.Api.PoolIdConst);
        ApplyComponentsByKind(workspace, blobs, stringTable, PrefabInstancePropertyOverridesComponent.Api.PoolIdConst);
        ApplyComponentsByKind(workspace, blobs, stringTable, TextComponent.Api.PoolIdConst);
        ApplyComponentsByKind(workspace, blobs, stringTable, AnimationLibraryComponent.Api.PoolIdConst);
        ApplyComponentsByKind(workspace, blobs, stringTable, StateMachineDefinitionComponent.Api.PoolIdConst);

        for (int i = 0; i < blobs.Count; i++)
        {
            ComponentBlob blob = blobs[i];

            if (blob.Kind == PrefabVariablesComponent.Api.PoolIdConst ||
                blob.Kind == PrefabInstanceComponent.Api.PoolIdConst ||
                blob.Kind == PrefabListDataComponent.Api.PoolIdConst ||
                blob.Kind == PrefabInstancePropertyOverridesComponent.Api.PoolIdConst ||
                blob.Kind == TextComponent.Api.PoolIdConst ||
                blob.Kind == AnimationLibraryComponent.Api.PoolIdConst ||
                blob.Kind == StateMachineDefinitionComponent.Api.PoolIdConst)
            {
                continue;
            }

            if (!UiComponentClipboardPacker.TryUnpack(workspace.PropertyWorld, blob.Component, blob.Bytes))
            {
                throw new InvalidOperationException("Failed to unpack component.");
            }
        }
    }

    private static void ApplyComponentsByKind(UiWorkspace workspace, List<ComponentBlob> blobs, IReadOnlyList<string> stringTable, ushort kind)
    {
        for (int i = 0; i < blobs.Count; i++)
        {
            ComponentBlob blob = blobs[i];
            if (blob.Kind != kind)
            {
                continue;
            }

            UiDocumentStringPatcher.UnpatchPackedComponentForLoad(workspace, blob.Entity, blob.Kind, blob.Bytes, stringTable);

            if (!UiComponentClipboardPacker.TryUnpack(workspace.PropertyWorld, blob.Component, blob.Bytes))
            {
                throw new InvalidOperationException("Failed to unpack component.");
            }
        }
    }

    private static void WriteEditorLayersChunk(BinaryWriter writer, UiWorkspace workspace, IReadOnlyList<string> stringTable)
    {
        WriteChunk(writer, "LAYR".AsSpan(), chunkVersion: 1, writePayload: w =>
        {
            int count = workspace._editorLayersDocument.NameByStableId.Count;
            w.Write(count);

            for (int stableId = 1; stableId < count; stableId++)
            {
                byte expanded = stableId < workspace._editorLayersDocument.ExpandedByStableId.Count ? workspace._editorLayersDocument.ExpandedByStableId[stableId] : (byte)0;
                byte hidden = stableId < workspace._editorLayersDocument.HiddenByStableId.Count ? workspace._editorLayersDocument.HiddenByStableId[stableId] : (byte)0;
                byte locked = stableId < workspace._editorLayersDocument.LockedByStableId.Count ? workspace._editorLayersDocument.LockedByStableId[stableId] : (byte)0;
                string name = stableId < workspace._editorLayersDocument.NameByStableId.Count ? workspace._editorLayersDocument.NameByStableId[stableId] : string.Empty;

                int nameIndex = IndexOfString(stringTable, name);
                w.Write(expanded);
                w.Write(hidden);
                w.Write(locked);
                w.Write(nameIndex);
            }
        });
    }

    private static void ReadEditorLayersChunk(BinaryReader reader, ReadOnlySpan<char> expectedId, UiWorkspace workspace, IReadOnlyList<string> stringTable)
    {
        ExpectChunk(reader, expectedId, out int chunkVersion, out int payloadLength);
        if (chunkVersion != 1)
        {
            throw new InvalidOperationException("Unsupported LAYR chunk version.");
        }

        long start = reader.BaseStream.Position;

        int count = reader.ReadInt32();
        if (count < 1)
        {
            throw new InvalidOperationException("Invalid LAYR entry count.");
        }

        workspace._editorLayersDocument.Reset();

        while (workspace._editorLayersDocument.NameByStableId.Count < count)
        {
            workspace._editorLayersDocument.ExpandedByStableId.Add(0);
            workspace._editorLayersDocument.HiddenByStableId.Add(0);
            workspace._editorLayersDocument.LockedByStableId.Add(0);
            workspace._editorLayersDocument.NameByStableId.Add(string.Empty);
        }

        for (int stableId = 1; stableId < count; stableId++)
        {
            workspace._editorLayersDocument.ExpandedByStableId[stableId] = reader.ReadByte();
            workspace._editorLayersDocument.HiddenByStableId[stableId] = reader.ReadByte();
            workspace._editorLayersDocument.LockedByStableId[stableId] = reader.ReadByte();
            int nameIndex = reader.ReadInt32();
            if ((uint)nameIndex < (uint)stringTable.Count)
            {
                workspace._editorLayersDocument.NameByStableId[stableId] = stringTable[nameIndex];
            }
            else
            {
                workspace._editorLayersDocument.NameByStableId[stableId] = string.Empty;
            }
        }

        EnsureChunkConsumed(reader, start, payloadLength);
    }

    private static int IndexOfString(IReadOnlyList<string> strings, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        for (int i = 0; i < strings.Count; i++)
        {
            if (string.Equals(strings[i], value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return 0;
    }

    private static void EnsureChunkConsumed(BinaryReader reader, long start, int payloadLength)
    {
        long consumed = reader.BaseStream.Position - start;
        if (consumed != payloadLength)
        {
            reader.BaseStream.Position = start + payloadLength;
        }
    }
}
