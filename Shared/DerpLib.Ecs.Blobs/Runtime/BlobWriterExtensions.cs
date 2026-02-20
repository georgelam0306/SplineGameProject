using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DerpLib.Ecs.Blobs;

public static class BlobWriterExtensions
{
    public static BlobHandle AddLengthPrefixedSpan<T>(this IBlobWriter writer, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        int count = data.Length;
        if (count <= 0)
        {
            return BlobHandle.Invalid;
        }

        int elementSize = Unsafe.SizeOf<T>();
        int payloadBytes = count * elementSize;
        int totalBytes = sizeof(int) + payloadBytes;

        byte[] bytes = new byte[totalBytes];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, count);
        ReadOnlySpan<byte> payload = MemoryMarshal.AsBytes(data);
        payload.CopyTo(bytes.AsSpan(sizeof(int)));

        return writer.Add(bytes);
    }
}

