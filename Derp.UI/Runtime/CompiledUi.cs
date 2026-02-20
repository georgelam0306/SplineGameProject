using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Derp.UI;

public sealed class CompiledUi
{
    public int Version { get; }
    public string[] Strings { get; }
    public Prefab[] Prefabs { get; }
    internal Node[] Nodes { get; }
    internal Component[] Components { get; }

    internal CompiledUi(int version, string[] strings, Prefab[] prefabs, Node[] nodes, Component[] components)
    {
        Version = version;
        Strings = strings;
        Prefabs = prefabs;
        Nodes = nodes;
        Components = components;
    }

    public readonly struct Prefab
    {
        public readonly uint StableId;
        public readonly int NameStringIndex;
        public readonly int RootNodeIndex;

        public Prefab(uint stableId, int nameStringIndex, int rootNodeIndex)
        {
            StableId = stableId;
            NameStringIndex = nameStringIndex;
            RootNodeIndex = rootNodeIndex;
        }
    }

    internal readonly struct Node
    {
        public readonly uint StableId;
        public readonly UiNodeType NodeType;
        public readonly int FirstChildIndex;
        public readonly int ChildCount;
        public readonly int FirstComponentIndex;
        public readonly int ComponentCount;

        public Node(uint stableId, UiNodeType nodeType, int firstChildIndex, int childCount, int firstComponentIndex, int componentCount)
        {
            StableId = stableId;
            NodeType = nodeType;
            FirstChildIndex = firstChildIndex;
            ChildCount = childCount;
            FirstComponentIndex = firstComponentIndex;
            ComponentCount = componentCount;
        }
    }

    internal readonly struct Component
    {
        public readonly ushort Kind;
        public readonly byte[] Bytes;

        public Component(ushort kind, byte[] bytes)
        {
            Kind = kind;
            Bytes = bytes;
        }
    }

    internal static CompiledUi FromBduiPayload(byte[] payload)
    {
        if (payload == null) throw new ArgumentNullException(nameof(payload));

        using var ms = new MemoryStream(payload, writable: false);
        using var reader = new BinaryReader(ms, new UTF8Encoding(false, true), leaveOpen: true);

        uint magic = reader.ReadUInt32();
        if (magic != 0x49554442) // "BDUI"
        {
            throw new InvalidOperationException("Invalid .bdui payload.");
        }

        int version = reader.ReadInt32();
        if (version != 1 && version != 2)
        {
            throw new InvalidOperationException("Unsupported .bdui version.");
        }

        int stringCount = reader.ReadInt32();
        if (stringCount < 1)
        {
            throw new InvalidOperationException("Invalid string table.");
        }

        var strings = new string[stringCount];
        for (int i = 0; i < stringCount; i++)
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

            strings[i] = len == 0 ? string.Empty : Encoding.UTF8.GetString(bytes);
        }

        if (version == 1)
        {
            return ReadV1(strings, reader);
        }

        return ReadV2(strings, reader);
    }

    private static CompiledUi ReadV1(string[] strings, BinaryReader reader)
    {
        int nodeCountV1 = reader.ReadInt32();
        if (nodeCountV1 <= 0)
        {
            throw new InvalidOperationException("Invalid node count.");
        }

        var nodesV1 = new Node[nodeCountV1];
        var components = new List<Component>(capacity: 512);

        for (int nodeIndex = 0; nodeIndex < nodeCountV1; nodeIndex++)
        {
            UiNodeType nodeType = (UiNodeType)reader.ReadByte();
            int childCount = reader.ReadInt32();
            if (childCount < 0)
            {
                throw new InvalidOperationException("Invalid child count.");
            }

            ushort componentCount = reader.ReadUInt16();
            int firstComponentIndex = components.Count;
            for (int i = 0; i < componentCount; i++)
            {
                ushort kind = reader.ReadUInt16();
                int size = reader.ReadInt32();
                if (size <= 0)
                {
                    throw new InvalidOperationException("Invalid component size.");
                }

                byte[] bytes = reader.ReadBytes(size);
                if (bytes.Length != size)
                {
                    throw new EndOfStreamException();
                }

                components.Add(new Component(kind, bytes));
            }

            int firstChildIndex = childCount > 0 ? nodeIndex + 1 : -1;
            nodesV1[nodeIndex] = new Node(stableId: 0, nodeType, firstChildIndex, childCount, firstComponentIndex, componentCount);
        }

        ValidateSingleRootPreorder(nodesV1);

        // Upgrade v1 format to a v2-like in-memory representation with a virtual root and a single prefab entry.
        var nodes = new Node[nodeCountV1 + 1];
        nodes[0] = new Node(stableId: 0, UiNodeType.None, firstChildIndex: 1, childCount: 1, firstComponentIndex: 0, componentCount: 0);
        Array.Copy(nodesV1, 0, nodes, 1, nodeCountV1);

        var prefabs = new Prefab[1];
        prefabs[0] = new Prefab(stableId: 0, nameStringIndex: 0, rootNodeIndex: 1);

        return new CompiledUi(version: 1, strings, prefabs, nodes, components.ToArray());
    }

    private static CompiledUi ReadV2(string[] strings, BinaryReader reader)
    {
        int prefabCount = reader.ReadInt32();
        if (prefabCount <= 0)
        {
            throw new InvalidOperationException("Invalid prefab count.");
        }

        var prefabs = new Prefab[prefabCount];
        for (int i = 0; i < prefabCount; i++)
        {
            uint stableId = reader.ReadUInt32();
            int nameStringIndex = reader.ReadInt32();
            int rootNodeIndex = reader.ReadInt32();
            prefabs[i] = new Prefab(stableId, nameStringIndex, rootNodeIndex);
        }

        int nodeCount = reader.ReadInt32();
        if (nodeCount <= 0)
        {
            throw new InvalidOperationException("Invalid node count.");
        }

        var nodes = new Node[nodeCount];
        var components = new List<Component>(capacity: 512);

        for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
        {
            uint stableId = reader.ReadUInt32();
            UiNodeType nodeType = (UiNodeType)reader.ReadByte();
            int childCount = reader.ReadInt32();
            if (childCount < 0)
            {
                throw new InvalidOperationException("Invalid child count.");
            }

            ushort componentCount = reader.ReadUInt16();
            int firstComponentIndex = components.Count;
            for (int i = 0; i < componentCount; i++)
            {
                ushort kind = reader.ReadUInt16();
                int size = reader.ReadInt32();
                if (size <= 0)
                {
                    throw new InvalidOperationException("Invalid component size.");
                }

                byte[] bytes = reader.ReadBytes(size);
                if (bytes.Length != size)
                {
                    throw new EndOfStreamException();
                }

                components.Add(new Component(kind, bytes));
            }

            int firstChildIndex = childCount > 0 ? nodeIndex + 1 : -1;
            nodes[nodeIndex] = new Node(stableId, nodeType, firstChildIndex, childCount, firstComponentIndex, componentCount);
        }

        ValidateSingleRootPreorder(nodes);
        return new CompiledUi(version: 2, strings, prefabs, nodes, components.ToArray());
    }

    private static void ValidateSingleRootPreorder(Node[] nodes)
    {
        if (nodes.Length == 0)
        {
            throw new InvalidOperationException("Invalid node list.");
        }

        var remainingChildren = new Stack<int>(capacity: 64);
        remainingChildren.Push(nodes[0].ChildCount);

        for (int i = 1; i < nodes.Length; i++)
        {
            while (remainingChildren.Count > 0 && remainingChildren.Peek() == 0)
            {
                remainingChildren.Pop();
            }

            if (remainingChildren.Count == 0)
            {
                throw new InvalidOperationException("Invalid .bdui: multiple roots detected.");
            }

            int parentRemaining = remainingChildren.Pop();
            remainingChildren.Push(parentRemaining - 1);

            if (nodes[i].ChildCount > 0)
            {
                remainingChildren.Push(nodes[i].ChildCount);
            }
        }

        while (remainingChildren.Count > 0 && remainingChildren.Peek() == 0)
        {
            remainingChildren.Pop();
        }

        if (remainingChildren.Count != 0)
        {
            throw new InvalidOperationException("Invalid .bdui: truncated node list.");
        }
    }
}
