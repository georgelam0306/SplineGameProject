using System;

namespace DerpLib.Ecs.Blobs;

public interface IBlobWriter
{
    BlobHandle Add(ReadOnlySpan<byte> bytes);
}

