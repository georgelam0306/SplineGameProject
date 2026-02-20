using System.Security.Cryptography;
using System.Text.Json;
using K4os.Compression.LZ4;

namespace DerpLib.AssetPipeline;

public sealed class FileObjectDatabase : IObjectDatabase
{
    private readonly string _objectsRoot;
    private readonly Dictionary<string, byte[]> _bundleCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loadedBundles = new(StringComparer.Ordinal);
    private string BundlesRoot => Path.Combine(Path.GetDirectoryName(_objectsRoot)!, "bundles");

    public FileObjectDatabase(string objectsRoot)
    {
        _objectsRoot = Path.GetFullPath(objectsRoot);
        Directory.CreateDirectory(_objectsRoot);
    }

    public ObjectId Put(byte[] data)
    {
        var hash = SHA256.HashData(data);
        var id = new ObjectId(Convert.ToHexString(hash));
        var path = Path.Combine(_objectsRoot, id.Value);
        try
        {
            using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            fs.Write(data, 0, data.Length);
        }
        catch (IOException)
        {
            // assume already created by parallel path
        }
        return id;
    }

    public byte[] Get(ObjectId id)
    {
        var path = Path.Combine(_objectsRoot, id.Value);
        if (File.Exists(path)) return File.ReadAllBytes(path);
        if (_bundleCache.TryGetValue(id.Value, out var bytes)) return bytes;
        throw new FileNotFoundException(id.Value);
    }

    public bool Has(ObjectId id)
    {
        var path = Path.Combine(_objectsRoot, id.Value);
        if (File.Exists(path)) return true;
        return _bundleCache.ContainsKey(id.Value);
    }

    public IEnumerable<ObjectId> EnumerateLooseObjects()
    {
        if (!Directory.Exists(_objectsRoot)) yield break;
        foreach (var file in Directory.GetFiles(_objectsRoot))
        {
            yield return new ObjectId(Path.GetFileName(file));
        }
    }

    public void CreateBundle(string name, IEnumerable<ObjectId> ids, bool compress = true)
    {
        Directory.CreateDirectory(BundlesRoot);
        var bundlePath = Path.Combine(BundlesRoot, name + ".json");

        var bundle = new BundleData { Compress = compress ? "lz4" : "none" };

        // Preserve existing entries if appending
        if (File.Exists(bundlePath))
        {
            var existing = JsonSerializer.Deserialize(
                File.ReadAllText(bundlePath),
                StorageJsonContext.Default.BundleData);
            if (existing != null)
            {
                bundle.Compress = existing.Compress;
                bundle.Profile = existing.Profile;
                foreach (var kv in existing.Objects)
                    bundle.Objects[kv.Key] = kv.Value;
            }
        }

        foreach (var id in ids)
        {
            if (bundle.Objects.ContainsKey(id.Value)) continue;
            var data = Get(id);

            byte[] encoded = bundle.Compress == "lz4" ? LZ4Pickler.Pickle(data) : data;
            bundle.Objects[id.Value] = Convert.ToBase64String(encoded);
        }

        var json = JsonSerializer.Serialize(bundle, StorageJsonContext.Default.BundleData);
        File.WriteAllText(bundlePath, json);
    }

    public void LoadBundle(string name)
    {
        var bundlePath = Path.Combine(BundlesRoot, name + ".json");
        if (!File.Exists(bundlePath)) throw new FileNotFoundException(bundlePath);

        var bundle = JsonSerializer.Deserialize(
            File.ReadAllText(bundlePath),
            StorageJsonContext.Default.BundleData);

        if (bundle == null) throw new InvalidDataException($"Invalid bundle: {bundlePath}");

        foreach (var kv in bundle.Objects)
        {
            var data = Convert.FromBase64String(kv.Value);

            if (bundle.Compress == "lz4")
            {
                data = LZ4Pickler.Unpickle(data);
            }

            _bundleCache[kv.Key] = data;
        }
        _loadedBundles.Add(name);
    }

    public void UnloadBundle(string name)
    {
        var bundlePath = Path.Combine(BundlesRoot, name + ".json");
        if (!File.Exists(bundlePath)) return;
        _bundleCache.Clear();
        _loadedBundles.Remove(name);
        foreach (var other in Directory.GetFiles(BundlesRoot, "*.json").Select(f => Path.GetFileNameWithoutExtension(f)))
        {
            if (_loadedBundles.Contains(other)) LoadBundle(other);
        }
    }

    public IEnumerable<string> LoadedBundles => _loadedBundles.ToArray();
}
