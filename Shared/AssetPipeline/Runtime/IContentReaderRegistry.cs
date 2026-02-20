namespace DerpLib.AssetPipeline;

public interface IContentReaderRegistry
{
    void Register(Type type, object reader);
    object? Resolve(Type runtimeType);
}
