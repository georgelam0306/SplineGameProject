namespace DerpLib.AssetPipeline;

[ContentReader(typeof(byte[]))]
public sealed class ByteArrayContentReader : IContentReader<byte[]>
{
    public int Version => 1;
    public byte[] Read(byte[] payload, IBlobSerializer serializer) => payload;
}
