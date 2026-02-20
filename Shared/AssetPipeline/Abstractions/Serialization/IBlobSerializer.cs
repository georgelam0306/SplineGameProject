namespace DerpLib.AssetPipeline;

public interface IBlobSerializer
{
    byte[] Serialize<T>(T obj);
    T Deserialize<T>(ReadOnlySpan<byte> bytes);
}
