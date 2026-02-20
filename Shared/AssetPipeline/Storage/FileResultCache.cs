using System.Text.Json;

namespace DerpLib.AssetPipeline;

public sealed class FileResultCache : ICache
{
    private readonly string _cachePath;
    private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);

    public FileResultCache(string cachePath)
    {
        _cachePath = Path.GetFullPath(cachePath);
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        if (File.Exists(_cachePath))
        {
            var json = File.ReadAllText(_cachePath);
            var dict = JsonSerializer.Deserialize(json, StorageJsonContext.Default.DictionaryStringString);
            if (dict != null)
            {
                foreach (var kv in dict) _map[kv.Key] = kv.Value;
            }
        }
    }

    public bool TryGet(string commandHash, out ObjectId id)
    {
        if (_map.TryGetValue(commandHash, out var value))
        {
            id = new ObjectId(value);
            return true;
        }
        id = default;
        return false;
    }

    public void Put(string commandHash, ObjectId resultId)
    {
        _map[commandHash] = resultId.Value;
        var json = JsonSerializer.Serialize(_map, StorageJsonContext.Default.DictionaryStringString);
        File.WriteAllText(_cachePath, json);
    }
}
