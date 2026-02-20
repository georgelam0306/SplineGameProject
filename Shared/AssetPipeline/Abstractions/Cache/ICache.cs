namespace DerpLib.AssetPipeline;

public interface ICache
{
    bool TryGet(string commandHash, out ObjectId id);
    void Put(string commandHash, ObjectId resultId);
}
