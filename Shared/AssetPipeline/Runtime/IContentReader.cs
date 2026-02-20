namespace DerpLib.AssetPipeline;

public interface IContentReader<T>
{
    int Version { get; }
    T Read(byte[] payload, IBlobSerializer serializer);
}
