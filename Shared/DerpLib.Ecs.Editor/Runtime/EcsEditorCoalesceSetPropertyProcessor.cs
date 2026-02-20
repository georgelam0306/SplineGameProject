using System;
using System.Buffers;

namespace DerpLib.Ecs.Editor;

public sealed class EcsEditorCoalesceSetPropertyProcessor : IEcsEditorCommandProcessor
{
    private readonly struct SetPropertyKey : IEquatable<SetPropertyKey>
    {
        public readonly ulong EntityValue;
        public readonly ulong ComponentSchemaId;
        public readonly ushort PropertyIndex;
        public readonly ulong PropertyId;
        public readonly byte PropertyKind;

        public SetPropertyKey(in EcsEditorCommand cmd)
        {
            EntityValue = cmd.Address.Entity.Value;
            ComponentSchemaId = cmd.Address.ComponentSchemaId;
            PropertyIndex = cmd.Address.PropertyIndex;
            PropertyId = cmd.Address.PropertyId;
            PropertyKind = (byte)cmd.PropertyKind;
        }

        public bool Equals(SetPropertyKey other)
        {
            return
                EntityValue == other.EntityValue &&
                ComponentSchemaId == other.ComponentSchemaId &&
                PropertyIndex == other.PropertyIndex &&
                PropertyId == other.PropertyId &&
                PropertyKind == other.PropertyKind;
        }
    }

    public void Process(ReadOnlySpan<EcsEditorCommand> input, EcsEditorCommandStream output)
    {
        if (input.Length == 0)
        {
            return;
        }

        int inputLength = input.Length;
        SetPropertyKey[] keyBuffer = ArrayPool<SetPropertyKey>.Shared.Rent(inputLength);
        int[] lastIndexByKey = ArrayPool<int>.Shared.Rent(inputLength);
        int uniqueKeyCount = 0;

        try
        {
            for (int i = 0; i < inputLength; i++)
            {
                ref readonly EcsEditorCommand cmd = ref input[i];
                if (cmd.Kind != EcsEditorCommandKind.SetProperty)
                {
                    continue;
                }

                var key = new SetPropertyKey(in cmd);
                int existingIndex = IndexOf(keyBuffer, uniqueKeyCount, in key);
                if (existingIndex < 0)
                {
                    keyBuffer[uniqueKeyCount] = key;
                    lastIndexByKey[uniqueKeyCount] = i;
                    uniqueKeyCount++;
                }
                else
                {
                    lastIndexByKey[existingIndex] = i;
                }
            }

            for (int i = 0; i < inputLength; i++)
            {
                ref readonly EcsEditorCommand cmd = ref input[i];
                if (cmd.Kind != EcsEditorCommandKind.SetProperty)
                {
                    output.Enqueue(in cmd);
                    continue;
                }

                var key = new SetPropertyKey(in cmd);
                int keyIndex = IndexOf(keyBuffer, uniqueKeyCount, in key);
                if (keyIndex < 0)
                {
                    continue;
                }

                if (lastIndexByKey[keyIndex] == i)
                {
                    output.Enqueue(in cmd);
                }
            }
        }
        finally
        {
            ArrayPool<SetPropertyKey>.Shared.Return(keyBuffer, clearArray: false);
            ArrayPool<int>.Shared.Return(lastIndexByKey, clearArray: false);
        }
    }

    private static int IndexOf(SetPropertyKey[] keys, int keyCount, in SetPropertyKey key)
    {
        for (int i = 0; i < keyCount; i++)
        {
            if (keys[i].Equals(key))
            {
                return i;
            }
        }

        return -1;
    }
}
