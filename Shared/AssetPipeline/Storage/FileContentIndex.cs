namespace DerpLib.AssetPipeline;

public sealed class FileContentIndex : IContentIndex
{
    private readonly string _indexPath;
    private readonly Dictionary<string, ObjectId> _map = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public FileContentIndex(string indexPath)
    {
        _indexPath = Path.GetFullPath(indexPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_indexPath)!);
        if (File.Exists(_indexPath))
        {
            foreach (var line in File.ReadAllLines(_indexPath))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2) _map[parts[0]] = new ObjectId(parts[1]);
            }
        }
    }

    public void Put(string url, ObjectId id)
    {
        lock (_gate) { _map[url] = id; }
    }

    public bool TryGet(string url, out ObjectId id)
    {
        lock (_gate) { return _map.TryGetValue(url, out id); }
    }

    public void Save()
    {
        KeyValuePair<string, ObjectId>[] snapshot;
        lock (_gate) { snapshot = _map.ToArray(); }
        var lines = snapshot.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                             .Select(kv => $"{kv.Key} {kv.Value.Value}");
        File.WriteAllLines(_indexPath, lines);
    }
}
