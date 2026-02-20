using System.Runtime.CompilerServices;

namespace DerpTech.Rollback;

public static class SnapshotSerializer
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteInt32(Span<byte> buffer, int offset, int value)
    {
        Unsafe.WriteUnaligned(ref buffer[offset], value);
        return offset + sizeof(int);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadInt32(ReadOnlySpan<byte> buffer, int offset, out int value)
    {
        value = Unsafe.ReadUnaligned<int>(ref Unsafe.AsRef(in buffer[offset]));
        return offset + sizeof(int);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteUInt64(Span<byte> buffer, int offset, ulong value)
    {
        Unsafe.WriteUnaligned(ref buffer[offset], value);
        return offset + sizeof(ulong);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadUInt64(ReadOnlySpan<byte> buffer, int offset, out ulong value)
    {
        value = Unsafe.ReadUnaligned<ulong>(ref Unsafe.AsRef(in buffer[offset]));
        return offset + sizeof(ulong);
    }
}
