using DerpLib.Vfs;
using Serilog;

namespace DerpLib.AssetPipeline;

public sealed class ContentManager
{
    private readonly VirtualFileSystem _vfs;
    private readonly IBlobSerializer _serializer;
    private readonly IContentReaderRegistry _readers;
    private readonly IObjectDatabase? _db;
    private readonly IContentIndex? _index;
    private readonly ILogger? _logger;
    private readonly Dictionary<string, WeakReference<object>> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Type> _typeRegistry = new(StringComparer.Ordinal);

    public ContentManager(VirtualFileSystem vfs, IBlobSerializer serializer)
        : this(vfs, serializer, new DefaultContentReaderRegistry(), null, null, null)
    { }

    public ContentManager(VirtualFileSystem vfs, IBlobSerializer serializer, IContentReaderRegistry readers)
        : this(vfs, serializer, readers, null, null, null)
    { }

    public ContentManager(
        VirtualFileSystem vfs,
        IBlobSerializer serializer,
        IContentReaderRegistry readers,
        IObjectDatabase? db,
        IContentIndex? index,
        ILogger? logger = null)
    {
        _vfs = vfs;
        _serializer = serializer;
        _readers = readers;
        _db = db;
        _index = index;
        _logger = logger;
    }

    /// <summary>
    /// Registers a content reader for a runtime type.
    /// </summary>
    public void RegisterReader(Type runtimeType, object reader)
    {
        if (runtimeType is null)
        {
            throw new ArgumentNullException(nameof(runtimeType));
        }

        if (reader is null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        _readers.Register(runtimeType, reader);
    }

    /// <summary>
    /// Registers a typed content reader for <typeparamref name="T"/>.
    /// </summary>
    public void RegisterReader<T>(IContentReader<T> reader)
    {
        if (reader is null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        _readers.Register(typeof(T), reader);
    }

    /// <summary>
    /// Registers a content type for AOT-compatible type resolution.
    /// Call this at startup for all types stored in chunk headers.
    /// </summary>
    public void RegisterType<T>()
    {
        var type = typeof(T);
        var name = type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
        _typeRegistry[name] = type;
    }

    /// <summary>
    /// Registers a content type with a specific type name.
    /// </summary>
    public void RegisterType(Type type, string? typeName = null)
    {
        var name = typeName ?? type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
        _typeRegistry[name] = type;
    }

    public T Load<T>(string url)
    {
        url = url.TrimStart('/');
        var cacheKey = typeof(T).AssemblyQualifiedName + "|" + url;
        if (_cache.TryGetValue(cacheKey, out var wr) && wr.TryGetTarget(out var obj) && obj is T cached)
        {
            _logger?.Debug("Content cache hit {Url} as {Type}", url, typeof(T).Name);
            return cached;
        }

        using var s = _vfs.OpenStream($"/data/db/{url}", VirtualFileMode.Open, VirtualFileAccess.Read);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        var data = ms.ToArray();

        // Detect chunk header; fallback to JSON payload
        if (ChunkHeader.TryParse(data, out var header))
        {
            // Try type registry first (AOT-safe), fall back to typeof(T)
            Type runtimeType;
            if (_typeRegistry.TryGetValue(header.TypeName, out var registeredType))
            {
                runtimeType = registeredType;
            }
            else
            {
                // Fall back to requested type T if not in registry
                _logger?.Warning("Type {TypeName} not registered, falling back to requested type {T}", header.TypeName, typeof(T).Name);
                runtimeType = typeof(T);
            }

            if (!typeof(T).IsAssignableFrom(runtimeType))
            {
                throw new InvalidOperationException($"Requested type {typeof(T).FullName} does not match content type {runtimeType.FullName}");
            }
            var payload = new ReadOnlySpan<byte>(data, header.OffsetToObject, data.Length - header.OffsetToObject).ToArray();
            var readerObj = _readers.Resolve(runtimeType);
            if (readerObj is null)
            {
                // Default JSON fallback: deserialize to T
                var val = _serializer.Deserialize<T>(payload);
                _cache[cacheKey] = new WeakReference<object>(val!);
                return val;
            }
            else if (readerObj is IContentReader<T> typedReader)
            {
                // Use typed reader (AOT-safe)
                var val = typedReader.Read(payload, _serializer);
                _cache[cacheKey] = new WeakReference<object>(val!);
                return val;
            }
            else
            {
                // Reader is registered but not as IContentReader<T> - type mismatch
                throw new InvalidOperationException(
                    $"Content reader for '{runtimeType.FullName}' is not compatible with requested type '{typeof(T).FullName}'. " +
                    $"Ensure the reader implements IContentReader<{typeof(T).Name}>.");
            }
        }
        else
        {
            var val = _serializer.Deserialize<T>(data);
            _cache[cacheKey] = new WeakReference<object>(val!);
            return val;
        }
    }

    public byte[] LoadBytes(string url)
    {
        url = url.TrimStart('/');
        using var s = _vfs.OpenStream($"/data/db/{url}", VirtualFileMode.Open, VirtualFileAccess.Read);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    // Variant-aware load: tries url@profile(s), then base url
    public T LoadVariant<T>(string baseUrl, params string[] profiles)
    {
        foreach (var p in profiles ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var candidate = baseUrl + "@" + p;
            try { return Load<T>(candidate); } catch { }
        }
        // Fallback to base URL
        return Load<T>(baseUrl);
    }

    public byte[] LoadBytesVariant(string baseUrl, params string[] profiles)
    {
        foreach (var p in profiles ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var candidate = baseUrl + "@" + p;
            try { return LoadBytes(candidate); } catch { }
        }
        return LoadBytes(baseUrl);
    }

    public ObjectId Save<T>(string url, T obj)
    {
        if (_db is null || _index is null)
            throw new NotSupportedException("ContentManager.Save requires a database and index to be provided in the constructor.");
        var bytes = ChunkHeader.Write(_serializer, obj);
        var id = _db.Put(bytes);
        _index.Put(url, id);
        _index.Save();
        return id;
    }

    public bool Exists(string url)
    {
        try
        {
            url = url.TrimStart('/');
            return _vfs.FileExists($"/data/db/{url}");
        }
        catch
        {
            return false;
        }
    }

    public void Reload(string url) => Invalidate(url);

    private void Invalidate(string url)
    {
        url = url.TrimStart('/');
        var suffix = "|" + url;
        var keys = _cache.Keys.Where(k => k.EndsWith(suffix, StringComparison.Ordinal)).ToArray();
        foreach (var k in keys) _cache.Remove(k);
        _logger?.Debug("Content cache invalidated {Url}", url);
    }
}
