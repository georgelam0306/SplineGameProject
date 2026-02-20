namespace DerpLib.AssetPipeline;

public sealed class JsonContentReader<T> : IContentReader<T>
{
    public int Version => 1;
    public T Read(byte[] payload, IBlobSerializer serializer)
        => serializer.Deserialize<T>(payload);
}
