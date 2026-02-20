using System.Text.Json;

namespace DerpLib.AssetPipeline;

public sealed class FileBuildRecords : IBuildRecords
{
    private readonly string _dir;

    public FileBuildRecords(string rootDir)
    {
        _dir = Path.GetFullPath(Path.Combine(rootDir, "records"));
        Directory.CreateDirectory(_dir);
    }

    public bool TryGet(string hash, out BuildCommandRecord record)
    {
        var path = Path.Combine(_dir, hash + ".json");
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            record = JsonSerializer.Deserialize(json, StorageJsonContext.Default.BuildCommandRecord)!;
            return true;
        }
        record = default!;
        return false;
    }

    public void Put(string hash, BuildCommandRecord record)
    {
        var path = Path.Combine(_dir, hash + ".json");
        var json = JsonSerializer.Serialize(record, StorageJsonContext.Default.BuildCommandRecord);
        File.WriteAllText(path, json);
    }

    public bool OutputsPresent(BuildCommandRecord record, IContentIndex index, IObjectDatabase objects)
    {
        foreach (var url in record.Outputs)
        {
            if (!index.TryGet(url, out var id)) return false;
            if (!objects.Has(id)) return false;
        }
        return true;
    }
}
