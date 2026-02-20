namespace DerpLib.AssetPipeline;

public interface IContentIndex
{
    void Put(string url, ObjectId id);
    bool TryGet(string url, out ObjectId id);
    void Save();
}
