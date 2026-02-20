using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace DerpLib.AssetPipeline;

/// <summary>
/// AOT-compatible JSON blob serializer.
/// Types must be registered via RegisterType before use.
/// </summary>
public sealed class JsonBlobSerializer : IBlobSerializer
{
    private readonly Dictionary<Type, JsonTypeInfo> _typeInfos = new();

    /// <summary>
    /// Registers a type for AOT-compatible serialization.
    /// Call this at startup for all types that will be serialized.
    /// </summary>
    public void RegisterType<T>(JsonTypeInfo<T> typeInfo)
    {
        _typeInfos[typeof(T)] = typeInfo;
    }

    /// <summary>
    /// Registers a type for AOT-compatible serialization using non-generic JsonTypeInfo.
    /// </summary>
    public void RegisterType(Type type, JsonTypeInfo typeInfo)
    {
        _typeInfos[type] = typeInfo;
    }

    public byte[] Serialize<T>(T obj)
    {
        if (!_typeInfos.TryGetValue(typeof(T), out var typeInfo))
        {
            throw new InvalidOperationException(
                $"Type '{typeof(T).FullName}' is not registered. " +
                $"Call RegisterType<{typeof(T).Name}>(jsonTypeInfo) at startup for AOT compatibility.");
        }

        return JsonSerializer.SerializeToUtf8Bytes(obj, (JsonTypeInfo<T>)typeInfo);
    }

    public T Deserialize<T>(ReadOnlySpan<byte> bytes)
    {
        if (!_typeInfos.TryGetValue(typeof(T), out var typeInfo))
        {
            throw new InvalidOperationException(
                $"Type '{typeof(T).FullName}' is not registered. " +
                $"Call RegisterType<{typeof(T).Name}>(jsonTypeInfo) at startup for AOT compatibility.");
        }

        return JsonSerializer.Deserialize(bytes, (JsonTypeInfo<T>)typeInfo)!;
    }
}
