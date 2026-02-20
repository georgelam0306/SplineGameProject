using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DerpLib.Ecs.Blobs;

public static class BlobStoreExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<T> ReadSpan<T>(this IBlobStore store, BlobHandle handle, int byteOffset, int elementCount)
        where T : unmanaged
    {
        ReadOnlySpan<byte> bytes = store.GetBytes(handle);
        ReadOnlySpan<byte> slice = bytes.Slice(byteOffset, elementCount * Unsafe.SizeOf<T>());
        return MemoryMarshal.Cast<byte, T>(slice);
    }

    public static int ReadLengthPrefixedCount(this IBlobStore store, BlobHandle handle)
    {
        ReadOnlySpan<byte> bytes = store.GetBytes(handle);
        if (bytes.Length < sizeof(int))
        {
            return 0;
        }

        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }

    public static ReadOnlySpan<T> ReadLengthPrefixedSpan<T>(this IBlobStore store, BlobHandle handle)
        where T : unmanaged
    {
        ReadOnlySpan<byte> bytes = store.GetBytes(handle);
        if (bytes.Length < sizeof(int))
        {
            return ReadOnlySpan<T>.Empty;
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        if (count <= 0)
        {
            return ReadOnlySpan<T>.Empty;
        }

        int elementSize = Unsafe.SizeOf<T>();
        int payloadBytes = count * elementSize;
        int requiredBytes = sizeof(int) + payloadBytes;
        if ((uint)requiredBytes > (uint)bytes.Length)
        {
            return ReadOnlySpan<T>.Empty;
        }

        ReadOnlySpan<byte> payload = bytes.Slice(sizeof(int), payloadBytes);
        return MemoryMarshal.Cast<byte, T>(payload);
    }
}
