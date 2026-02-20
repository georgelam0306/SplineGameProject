namespace DerpLib.AssetPipeline;

public interface IAssetCompiler
{
    IEnumerable<string> GetInputFiles(AssetItem item);
    ObjectId Compile(AssetItem item, IObjectDatabase db, IBlobSerializer serializer);
}
