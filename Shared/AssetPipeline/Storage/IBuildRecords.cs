namespace DerpLib.AssetPipeline;

public interface IBuildRecords
{
    bool TryGet(string hash, out BuildCommandRecord record);
    void Put(string hash, BuildCommandRecord record);
    bool OutputsPresent(BuildCommandRecord record, IContentIndex index, IObjectDatabase objects);
}
