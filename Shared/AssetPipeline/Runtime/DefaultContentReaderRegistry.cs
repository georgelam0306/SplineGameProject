namespace DerpLib.AssetPipeline;

public sealed class DefaultContentReaderRegistry : IContentReaderRegistry
{
    private readonly Dictionary<Type, object> _map = new();

    public void Register(Type type, object reader) => _map[type] = reader;
    public object? Resolve(Type runtimeType) => _map.TryGetValue(runtimeType, out var r) ? r : null;
    public void RegisterJsonFallback<T>() => Register(typeof(T), new JsonContentReader<T>());
}
