using System;
using System.Collections.Generic;

namespace DerpLib.Ecs.Blobs;

/// <summary>
/// Simple blob store intended for tooling/tests. Not optimized.
/// </summary>
public sealed class ByteArrayBlobStore : IBlobStore, IBlobWriter
{
    private readonly Dictionary<ulong, byte[]> _blobs = new();
    private ulong _nextId = 1;

    public BlobHandle Add(byte[] bytes)
    {
        return Add(bytes.AsSpan());
    }

    public BlobHandle Add(ReadOnlySpan<byte> bytes)
    {
        ulong id = _nextId++;
        _blobs.Add(id, bytes.ToArray());
        return new BlobHandle(id);
    }

    public ReadOnlySpan<byte> GetBytes(BlobHandle handle)
    {
        if (!handle.IsValid)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        if (_blobs.TryGetValue(handle.Value, out byte[]? bytes))
        {
            return bytes;
        }

        return ReadOnlySpan<byte>.Empty;
    }
}
