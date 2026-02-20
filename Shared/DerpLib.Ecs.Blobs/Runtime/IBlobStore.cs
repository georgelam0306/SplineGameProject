using System;

namespace DerpLib.Ecs.Blobs;

public interface IBlobStore
{
    ReadOnlySpan<byte> GetBytes(BlobHandle handle);
}

