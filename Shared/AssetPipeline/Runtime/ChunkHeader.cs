using System.Text;

namespace DerpLib.AssetPipeline;

public sealed class ChunkHeader
{
    public const string Magic = "CHNK";
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public string TypeName { get; set; } = string.Empty;
    public int OffsetToObject { get; set; }
    public int OffsetToReferences { get; set; } = -1;

    public static bool TryParse(ReadOnlySpan<byte> data, out ChunkHeader header)
    {
        header = default!;
        if (data.Length < 4) return false;
        if (Encoding.ASCII.GetString(data.Slice(0, 4)) != Magic) return false;
        var span = data;
        int pos = 4;
        if (!ReadInt(span, ref pos, out var version)) return false;
        if (!ReadString(span, ref pos, out var typeName)) return false;
        if (!ReadInt(span, ref pos, out var offObj)) return false;
        if (!ReadInt(span, ref pos, out var offRefs)) return false;
        header = new ChunkHeader { Version = version, TypeName = typeName, OffsetToObject = offObj, OffsetToReferences = offRefs };
        return true;
    }

    public static byte[] Write<T>(IBlobSerializer serializer, T obj)
    {
        var payload = serializer.Serialize(obj);
        return Write(typeof(T), payload);
    }

    public static byte[] Write(Type runtimeType, byte[] payload)
    {
        var typeName = runtimeType.AssemblyQualifiedName ?? runtimeType.FullName ?? runtimeType.Name;
        var typeBytes = Encoding.UTF8.GetBytes(typeName);
        int headerLen = 4 + 4 + 4 + typeBytes.Length + 4 + 4;
        using var ms = new MemoryStream(headerLen + payload.Length);
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        bw.Write(Encoding.ASCII.GetBytes(Magic));
        bw.Write(CurrentVersion);
        bw.Write(typeBytes.Length);
        bw.Write(typeBytes);
        bw.Write(headerLen);
        bw.Write(-1);
        bw.Write(payload);
        bw.Flush();
        return ms.ToArray();
    }

    private static bool ReadInt(ReadOnlySpan<byte> span, ref int pos, out int value)
    {
        value = default;
        if (pos + 4 > span.Length) return false;
        value = BitConverter.ToInt32(span.Slice(pos, 4));
        pos += 4;
        return true;
    }

    private static bool ReadString(ReadOnlySpan<byte> span, ref int pos, out string value)
    {
        value = string.Empty;
        if (!ReadInt(span, ref pos, out var len)) return false;
        if (len < 0 || pos + len > span.Length) return false;
        value = Encoding.UTF8.GetString(span.Slice(pos, len));
        pos += len;
        return true;
    }
}
